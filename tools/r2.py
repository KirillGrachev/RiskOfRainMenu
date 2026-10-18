"""Инструменты для чтения managed-сборки: индекс типов/методов + IL-дизассемблер.

Версия 2 — данные читаются собственным ридером ECMA-335 (md.py), а не dnfile.
Причина: dnfile на RoR2.dll (47406 методов) занимает ~580 МБ и ~9 секунд,
потому что создаёт объект на каждую строку каждой таблицы и на каждую ссылку
в MethodList/FieldList. md.py читает таблицы как сырые байты по схемам
ECMA-335: ~25 МБ и ~0,02 с.

Внешний API сохранён (a.tl.MethodDef.rows[i].Rva, a.full_type, a.m_owner,
a.disasm, ...), поэтому dump.py / ilpatch.py / verify.py / sigres.py работают
без изменений. Строки таблиц — ленивые прокси: поле читается в момент
обращения и нигде не накапливается.

Исправлено по сравнению с самыми первыми версиями:
  * цель ветвления считается от конца инструкции, а не от её начала;
  * отдельные карты владельцев для MethodDef и Field (раньше поля
    резолвились через карту методов -> мусорные имена);
  * нормальный декодер TypeSpec / MethodSpec сигнатур;
  * _ts_cache больше не классовый атрибут — кэш TypeSpec одной сборки
    раньше протекал в другую (при сравнении оригинала и патча).
"""
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from md import MetaData, MdError          # noqa: E402,F401

OP1 = {
    0x00: 'nop', 0x01: 'break', 0x02: 'ldarg.0', 0x03: 'ldarg.1', 0x04: 'ldarg.2',
    0x05: 'ldarg.3', 0x06: 'ldloc.0', 0x07: 'ldloc.1', 0x08: 'ldloc.2', 0x09: 'ldloc.3',
    0x0A: 'stloc.0', 0x0B: 'stloc.1', 0x0C: 'stloc.2', 0x0D: 'stloc.3', 0x0E: 'ldarg.s',
    0x0F: 'ldarga.s', 0x10: 'starg.s', 0x11: 'ldloc.s', 0x12: 'ldloca.s', 0x13: 'stloc.s',
    0x14: 'ldnull', 0x15: 'ldc.i4.m1', 0x16: 'ldc.i4.0', 0x17: 'ldc.i4.1', 0x18: 'ldc.i4.2',
    0x19: 'ldc.i4.3', 0x1A: 'ldc.i4.4', 0x1B: 'ldc.i4.5', 0x1C: 'ldc.i4.6', 0x1D: 'ldc.i4.7',
    0x1E: 'ldc.i4.8', 0x1F: 'ldc.i4.s', 0x20: 'ldc.i4', 0x21: 'ldc.i8', 0x22: 'ldc.r4',
    0x23: 'ldc.r8', 0x25: 'dup', 0x26: 'pop', 0x27: 'jmp', 0x28: 'call', 0x29: 'calli',
    0x2A: 'ret', 0x2B: 'br.s', 0x2C: 'brfalse.s', 0x2D: 'brtrue.s', 0x2E: 'beq.s',
    0x2F: 'bge.s', 0x30: 'bgt.s', 0x31: 'ble.s', 0x32: 'blt.s', 0x33: 'bne.un.s',
    0x34: 'bge.un.s', 0x35: 'bgt.un.s', 0x36: 'ble.un.s', 0x37: 'blt.un.s', 0x38: 'br',
    0x39: 'brfalse', 0x3A: 'brtrue', 0x3B: 'beq', 0x3C: 'bge', 0x3D: 'bgt', 0x3E: 'ble',
    0x3F: 'blt', 0x40: 'bne.un', 0x41: 'bge.un', 0x42: 'bgt.un', 0x43: 'ble.un',
    0x44: 'blt.un', 0x45: 'switch', 0x46: 'ldind.i1', 0x47: 'ldind.u1', 0x48: 'ldind.i2',
    0x49: 'ldind.u2', 0x4A: 'ldind.i4', 0x4B: 'ldind.u4', 0x4C: 'ldind.i8', 0x4D: 'ldind.i',
    0x4E: 'ldind.r4', 0x4F: 'ldind.r8', 0x50: 'ldind.ref', 0x51: 'stind.ref', 0x52: 'stind.i1',
    0x53: 'stind.i2', 0x54: 'stind.i4', 0x55: 'stind.i8', 0x56: 'stind.r4', 0x57: 'stind.r8',
    0x58: 'add', 0x59: 'sub', 0x5A: 'mul', 0x5B: 'div', 0x5C: 'div.un', 0x5D: 'rem',
    0x5E: 'rem.un', 0x5F: 'and', 0x60: 'or', 0x61: 'xor', 0x62: 'shl', 0x63: 'shr',
    0x64: 'shr.un', 0x65: 'neg', 0x66: 'not', 0x67: 'conv.i1', 0x68: 'conv.i2',
    0x69: 'conv.i4', 0x6A: 'conv.i8', 0x6B: 'conv.r4', 0x6C: 'conv.r8', 0x6D: 'conv.u4',
    0x6E: 'conv.u8', 0x6F: 'callvirt', 0x70: 'cpobj', 0x71: 'ldobj', 0x72: 'ldstr',
    0x73: 'newobj', 0x74: 'castclass', 0x75: 'isinst', 0x76: 'conv.r.un', 0x79: 'unbox',
    0x7A: 'throw', 0x7B: 'ldfld', 0x7C: 'ldflda', 0x7D: 'stfld', 0x7E: 'ldsfld',
    0x7F: 'ldsflda', 0x80: 'stsfld', 0x81: 'stobj', 0x82: 'conv.ovf.i1.un',
    0x83: 'conv.ovf.i2.un', 0x84: 'conv.ovf.i4.un', 0x85: 'conv.ovf.i8.un',
    0x86: 'conv.ovf.u1.un', 0x87: 'conv.ovf.u2.un', 0x88: 'conv.ovf.u4.un',
    0x89: 'conv.ovf.u8.un', 0x8A: 'conv.ovf.i.un', 0x8B: 'conv.ovf.u.un', 0x8C: 'box',
    0x8D: 'newarr', 0x8E: 'ldlen', 0x8F: 'ldelema', 0x90: 'ldelem.i1', 0x91: 'ldelem.u1',
    0x92: 'ldelem.i2', 0x93: 'ldelem.u2', 0x94: 'ldelem.i4', 0x95: 'ldelem.u4',
    0x96: 'ldelem.i8', 0x97: 'ldelem.i', 0x98: 'ldelem.r4', 0x99: 'ldelem.r8',
    0x9A: 'ldelem.ref', 0x9B: 'stelem.i', 0x9C: 'stelem.i1', 0x9D: 'stelem.i2',
    0x9E: 'stelem.i4', 0x9F: 'stelem.i8', 0xA0: 'stelem.r4', 0xA1: 'stelem.r8',
    0xA2: 'stelem.ref', 0xA3: 'ldelem', 0xA4: 'stelem', 0xA5: 'unbox.any',
    0xB3: 'conv.ovf.i1', 0xB4: 'conv.ovf.u1', 0xB5: 'conv.ovf.i2', 0xB6: 'conv.ovf.u2',
    0xB7: 'conv.ovf.i4', 0xB8: 'conv.ovf.u4', 0xB9: 'conv.ovf.i8', 0xBA: 'conv.ovf.u8',
    0xC2: 'refanyval', 0xC3: 'ckfinite', 0xC6: 'mkrefany', 0xD0: 'ldtoken', 0xD1: 'conv.u2',
    0xD2: 'conv.u1', 0xD3: 'conv.i', 0xD4: 'conv.ovf.i', 0xD5: 'conv.ovf.u', 0xD6: 'add.ovf',
    0xD7: 'add.ovf.un', 0xD8: 'mul.ovf', 0xD9: 'mul.ovf.un', 0xDA: 'sub.ovf',
    0xDB: 'sub.ovf.un', 0xDC: 'endfinally', 0xDD: 'leave', 0xDE: 'leave.s', 0xDF: 'endfilter',
    0xE0: 'unaligned.', 0xE1: 'volatile.', 0xE2: 'tail.', 0xE3: 'initobj', 0xE4: 'constrained.',
    0xE5: 'cpblk', 0xE6: 'initblk', 0xE8: 'rethrow', 0xEE: 'sizeof', 0xEF: 'refanytype',
    0xF0: 'readonly.',
}
OP2 = {
    0x00: 'arglist', 0x01: 'ceq', 0x02: 'cgt', 0x03: 'cgt.un', 0x04: 'clt', 0x05: 'clt.un',
    0x06: 'ldftn', 0x07: 'ldvirtftn', 0x09: 'ldarg', 0x0A: 'ldarga', 0x0B: 'starg',
    0x0C: 'ldloc', 0x0D: 'ldloca', 0x0E: 'stloc', 0x0F: 'localloc', 0x11: 'endfilter',
    0x12: 'unaligned.', 0x13: 'volatile.', 0x14: 'tail.', 0x15: 'initobj',
    0x16: 'constrained.', 0x17: 'cpblk', 0x18: 'initblk', 0x1A: 'rethrow', 0x1C: 'sizeof',
    0x1D: 'refanytype', 0x1E: 'readonly.',
}
TOK_OPS = {
    'jmp', 'call', 'calli', 'callvirt', 'newobj', 'ldfld', 'ldflda', 'stfld', 'ldsfld',
    'ldsflda', 'stsfld', 'ldtoken', 'castclass', 'isinst', 'box', 'unbox', 'unbox.any',
    'newarr', 'ldelema', 'ldelem', 'stelem', 'ldobj', 'stobj', 'cpobj', 'initobj',
    'constrained.', 'ldftn', 'ldvirtftn', 'sizeof', 'mkrefany', 'refanyval',
}
BR_OPS = {
    'br.s': 1, 'brfalse.s': 1, 'brtrue.s': 1, 'beq.s': 1, 'bge.s': 1, 'bgt.s': 1,
    'ble.s': 1, 'blt.s': 1, 'bne.un.s': 1, 'bge.un.s': 1, 'bgt.un.s': 1, 'ble.un.s': 1,
    'blt.un.s': 1, 'leave.s': 1,
    'br': 4, 'brfalse': 4, 'brtrue': 4, 'beq': 4, 'bge': 4, 'bgt': 4, 'ble': 4, 'blt': 4,
    'bne.un': 4, 'bge.un': 4, 'bgt.un': 4, 'ble.un': 4, 'blt.un': 4, 'leave': 4,
}
SHORT_OPS = {'ldarg.s', 'ldarga.s', 'starg.s', 'ldloc.s', 'ldloca.s', 'stloc.s',
             'ldc.i4.s', 'unaligned.'}
SHORT2_OPS = {'ldarg', 'ldarga', 'starg', 'ldloc', 'ldloca', 'stloc'}

ELEMENT_TYPE = {
    0x01: 'void', 0x02: 'boolean', 0x03: 'char', 0x04: 'i1', 0x05: 'u1', 0x06: 'i2',
    0x07: 'u2', 0x08: 'i4', 0x09: 'u4', 0x0A: 'i8', 0x0B: 'u8', 0x0C: 'r4', 0x0D: 'r8',
    0x0E: 'string', 0x16: 'typedbyref', 0x18: 'i', 0x19: 'u', 0x1C: 'object',
}



# ===========================================================================
# Совместимый слой: код, написанный под dnfile (a.tl.MethodDef.rows[i].Rva),
# продолжает работать, но строки конструируются лениво и живут только пока
# на них есть ссылка.
# ===========================================================================

#: поля-списки: указывают на первую строку сплошного блока
LIST_FIELDS = {'MethodList': 'MethodDef', 'FieldList': 'Field',
               'ParamList': 'Param', 'EventList': 'Event',
               'PropertyList': 'Property'}


class _Stub:
    """Заглушка-владелец имени таблицы: type(ref.table).__name__ == 'TypeRef'."""
    _cache = {}

    @classmethod
    def for_name(cls, name):
        obj = cls._cache.get(name)
        if obj is None:
            obj = type(name, (), {})()
            cls._cache[name] = obj
        return obj


class HeapRef:
    """Индекс в #Blob. Поддерживает int()/[.row_index], как объект dnfile."""
    __slots__ = ('value',)

    def __init__(self, value):
        self.value = value

    @property
    def row_index(self):
        return self.value

    def __int__(self):
        return self.value

    def __index__(self):
        return self.value

    def __repr__(self):
        return f'HeapRef({self.value:#x})'


class RowRef:
    """Ссылка на строку таблицы: .row_index и .row."""
    __slots__ = ('md', 'table', 'rid')

    def __init__(self, md, table, rid):
        self.md = md
        self.table = table
        self.rid = rid

    @property
    def row_index(self):
        return self.rid

    @property
    def row(self):
        return RowProxy(self.md, self.table, self.rid) if self.rid else None

    def __repr__(self):
        return f'RowRef({self.table}!{self.rid})'


class CodedRef:
    """Ссылка из coded index: знает и таблицу, и rid."""
    __slots__ = ('md', 'tid', 'rid')

    def __init__(self, md, tid, rid):
        self.md = md
        self.tid = tid
        self.rid = rid

    @property
    def row_index(self):
        return self.rid

    @property
    def table(self):
        return _Stub.for_name(self.md.table_id_name(self.tid)) if self.tid else None

    @property
    def row(self):
        if not self.rid or not self.tid:
            return None
        return RowProxy(self.md, self.md.table_id_name(self.tid), self.rid)

    def __repr__(self):
        return f'CodedRef({self.md.table_id_name(self.tid)}!{self.rid})'


class RowList:
    """Список ссылок на блок строк (MethodList / FieldList / ...)."""
    __slots__ = ('md', 'table', 'first', 'count')

    def __init__(self, md, table, first, count):
        self.md = md
        self.table = table
        self.first = first
        self.count = count

    @property
    def row_index(self):
        return self.first

    def __len__(self):
        return self.count

    def __iter__(self):
        for i in range(self.count):
            yield RowRef(self.md, self.table, self.first + i)

    def __getitem__(self, i):
        if isinstance(i, slice):
            return [RowRef(self.md, self.table, self.first + j)
                    for j in range(*i.indices(self.count))]
        if i < 0:
            i += self.count
        if not 0 <= i < self.count:
            raise IndexError(i)
        return RowRef(self.md, self.table, self.first + i)

    def __repr__(self):
        return f'RowList({self.table} {self.first}..{self.first + self.count - 1})'


class RowProxy:
    """Строка таблицы. Поля читаются по запросу, ничего не кэшируется пачками."""
    __slots__ = ('md', 'table', 'rid')

    def __init__(self, md, table, rid):
        object.__setattr__(self, 'md', md)
        object.__setattr__(self, 'table', table)
        object.__setattr__(self, 'rid', rid)

    @property
    def row_index(self):
        return self.rid

    def __getattr__(self, name):
        md, table, rid = self.md, self.table, self.rid
        ftype = md.field_type(table, name)
        raw = md.get(table, rid, name)
        if ftype == 'str':
            return md.string(raw)
        if ftype == 'guid':
            return md.guid(raw)
        if ftype == 'blob':
            return HeapRef(raw)
        if isinstance(ftype, tuple):
            if ftype[0] == 'idx':
                tgt = md.table_id_name(ftype[1])
                if name in LIST_FIELDS:
                    first, cnt = md.list_range(table, rid, name, tgt)
                    return RowList(md, tgt, first, cnt)
                return RowRef(md, tgt, raw)
            if ftype[0] == 'coded':
                tid, r = raw
                return CodedRef(md, tid, r)
        return raw

    def __repr__(self):
        return f'Row({self.table}!{self.rid})'


class RowsView:
    """rows[i] — как в dnfile, от нуля."""
    __slots__ = ('md', 'table')

    def __init__(self, md, table):
        self.md = md
        self.table = table

    def __len__(self):
        return self.md.count(self.table)

    def __getitem__(self, i):
        n = self.md.count(self.table)
        if isinstance(i, slice):
            return [RowProxy(self.md, self.table, j + 1) for j in range(*i.indices(n))]
        rid = i + 1 if i >= 0 else n + i + 1
        if not 1 <= rid <= n:
            raise IndexError(f'{self.table} rid {rid} вне 1..{n}')
        return RowProxy(self.md, self.table, rid)

    def __iter__(self):
        for i in range(self.md.count(self.table)):
            yield RowProxy(self.md, self.table, i + 1)


class TableProxy:
    __slots__ = ('md', 'name')

    def __init__(self, md, name):
        self.md = md
        self.name = name

    @property
    def rows(self):
        return RowsView(self.md, self.name)

    @property
    def row_size(self):
        return self.md.tables[self.name].row_size

    def __len__(self):
        return self.md.count(self.name)

    def __repr__(self):
        return f'<Table {self.name} rows={self.md.count(self.name)}>'


class TablesProxy:
    """a.tl.MethodDef / a.tl.TypeDef / ..."""
    __slots__ = ('md',)

    def __init__(self, md):
        self.md = md

    def __getattr__(self, name):
        if name in self.md.tables:
            return TableProxy(self.md, name)
        raise AttributeError(name)


class OwnerMap:
    """a.m_owner.get(rid, 0) — владелец метода/поля.

    Лениво: бинарный поиск по начальным RID блоков TypeDef, а не словарь
    на 47 тысяч записей, построенный обходом всей таблицы.
    """
    __slots__ = ('md', 'kind', '_cache')

    def __init__(self, md, kind):
        self.md = md
        self.kind = kind
        self._cache = {}

    def _owner(self, rid):
        c = self._cache
        if rid in c:
            return c[rid]
        v = (self.md.owner_of_method(rid) if self.kind == 'method'
             else self.md.owner_of_field(rid))
        c[rid] = v
        return v

    def get(self, rid, default=0):
        return self._owner(rid) or default

    def __getitem__(self, rid):
        return self._owner(rid)

    def __contains__(self, rid):
        return bool(self._owner(rid))

    def __len__(self):
        raise TypeError('ленивая карта владельцев не имеет размера')


# ===========================================================================
# Asm
# ===========================================================================
class Asm:
    def __init__(self, path):
        self.path = path
        self.md = MetaData(path)
        self.pe = self.md.pe
        self.tl = TablesProxy(self.md)
        self.data = self.md.data
        self.stream = dict(self.md.streams)
        self._ts_cache = {}
        self._build_index()

    def __repr__(self):
        return f'<Asm {os.path.basename(self.path)} methods={self.md.count("MethodDef")}>'

    # ---------------- PE / потоки ----------------
    def rva2off(self, rva):
        return self.md.rva_to_off(rva)

    def _str(self, heap, idx):
        off, size = self.stream[heap]
        d = self.data
        e = d.index(b'\0', off + idx)
        return d[off + idx:e].decode('utf-8', 'replace')

    def blob(self, idx):
        return self.md.blob(idx)

    def user_string(self, idx):
        return self.md.user_string(idx)

    # ---------------- индексы ----------------
    def _build_index(self):
        md = self.md
        self.nested = {}
        for i in range(1, md.count('NestedClass') + 1):
            self.nested[md.get('NestedClass', i, 'NestedClass')] = \
                md.get('NestedClass', i, 'EnclosingClass')

        self.type_name = {}
        for i in range(1, md.count('TypeDef') + 1):
            ns = md.name('TypeDef', i, 'TypeNamespace')
            nm = md.name('TypeDef', i, 'TypeName')
            self.type_name[i] = f'{ns}.{nm}' if ns else nm

        self.full_type = {}
        for i in self.type_name:
            parts = [self.type_name[i]]
            cur, guard = i, 0
            while cur in self.nested and guard < 50:
                cur = self.nested[cur]
                parts.append(self.type_name[cur])
                guard += 1
            self.full_type[i] = '+'.join(reversed(parts))

        self.by_name = {}
        for i, n in self.full_type.items():
            self.by_name.setdefault(n, []).append(i)

        self.m_owner = OwnerMap(md, 'method')
        self.f_owner = OwnerMap(md, 'field')

        self.assembly_ref = {i: md.name('AssemblyRef', i)
                             for i in range(1, md.count('AssemblyRef') + 1)}

    def find_type(self, sub):
        sub = sub.lower()
        return sorted((i, n) for i, n in self.full_type.items() if sub in n.lower())

    def find_methods(self, substr, tsubstr=None):
        res = []
        md = self.md
        substr = substr.lower()
        for i in range(1, md.count('MethodDef') + 1):
            nm = md.name('MethodDef', i)
            if substr in nm.lower():
                t = self.full_type.get(self.m_owner.get(i, 0), '?')
                if tsubstr and tsubstr.lower() not in t.lower():
                    continue
                res.append((i, t, nm))
        return res

    # ---------------- имена ----------------
    def method_full_name(self, mrid):
        """RoR2.UserProfile::HasUnlockable — без разбора сигнатуры."""
        return f'{self.full_type.get(self.m_owner.get(mrid, 0), "?")}::' \
               f'{self.md.name("MethodDef", mrid)}'

    def field_full_name(self, frid):
        return f'{self.full_type.get(self.f_owner.get(frid, 0), "?")}::' \
               f'{self.md.name("Field", frid)}'

    def type_ref_name(self, rid):
        ns = self.md.name('TypeRef', rid, 'TypeNamespace')
        nm = self.md.name('TypeRef', rid, 'TypeName')
        return f'{ns}.{nm}' if ns else nm

    def rva_of(self, mrid):
        return self.md.get('MethodDef', mrid, 'Rva')

    # ---------------- сигнатуры TypeSpec ----------------
    def _sig_type(self, b, p):
        """Возвращает (строка, новый p) — минимальный декодер #Blob сигнатур типов."""
        if p >= len(b):
            return '?', p
        t = b[p]; p += 1
        if t in ELEMENT_TYPE:
            return ELEMENT_TYPE[t], p
        if t in (0x11, 0x12):  # VALUETYPE / CLASS
            n, p = self._compr(b, p)
            return self._typedef_or_ref(n), p
        if t == 0x15:  # GENERICINST
            kind = b[p]; p += 1
            n, p = self._compr(b, p)
            base = self._typedef_or_ref(n)
            argc, p = self._compr(b, p)
            args = []
            for _ in range(argc):
                s, p = self._sig_type(b, p)
                args.append(s)
            return f"{base}<{','.join(args)}>", p
        if t == 0x13:  # VAR (параметр типа)
            n, p = self._compr(b, p)
            return f"!{n}", p
        if t == 0x14:  # ARRAY
            s, p = self._sig_type(b, p)
            rank, p = self._compr(b, p)
            nsz, p = self._compr(b, p)
            for _ in range(nsz):
                _, p = self._compr(b, p)
            nlo, p = self._compr(b, p)
            for _ in range(nlo):
                _, p = self._compr(b, p)
            return (f"{s}[]" if rank == 1 else f"{s}[{','.join([''] * rank)}]"), p
        if t == 0x1E:  # MVAR (параметр метода)
            n, p = self._compr(b, p)
            return f"!!{n}", p
        if t == 0x1D:  # SZARRAY
            s, p = self._sig_type(b, p)
            return f"{s}[]", p
        if t == 0x10:  # PTR
            s, p = self._sig_type(b, p)
            return f"{s}*", p
        if t == 0x0F:  # BYREF
            s, p = self._sig_type(b, p)
            return f"ref {s}", p
        if t == 0x20:  # CMOD_OPT
            n, p = self._compr(b, p)
            s, p = self._sig_type(b, p)
            return s, p
        if t == 0x1F:  # CMOD_REQ
            n, p = self._compr(b, p)
            s, p = self._sig_type(b, p)
            return s, p
        if t == 0x1C:
            return 'object', p
        return f"?{t:02x}", p

    @staticmethod
    def _compr(b, p):
        x = b[p]
        if x < 0x80:
            return x, p + 1
        if x < 0xC0:
            return ((x & 0x3F) << 8) | b[p + 1], p + 2
        return ((x & 0x1F) << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3], p + 4

    def _typedef_or_ref(self, coded):
        """TypeDefOrRefEncoded из сигнатуры -> имя типа."""
        tab = coded & 3
        rid = coded >> 2
        try:
            if tab == 0:
                return self.full_type.get(rid, f'TypeDef!{rid}')
            if tab == 1:
                return self.type_ref_name(rid)
            if tab == 2:
                return self.typespec_name(rid)
        except Exception:
            pass
        return f'?tr{tab}!{rid}'

    def typespec_name(self, rid):
        if rid in self._ts_cache:
            return self._ts_cache[rid]
        name = f'TypeSpec!{rid}'
        try:
            b = self.blob(self.md.get('TypeSpec', rid, 'Signature'))
            if b:
                name, _ = self._sig_type(b, 0)
        except Exception:
            pass
        self._ts_cache[rid] = name
        return name

    # ---------------- токены ----------------
    def _parent_name(self, tid, rid):
        """Имя владельца MemberRef (MemberRefParent)."""
        md = self.md
        if tid == 0x02:
            return self.full_type.get(rid, f'TypeDef!{rid}')
        if tid == 0x01:
            return self.type_ref_name(rid)
        if tid == 0x1A:
            return 'mod:' + md.name('ModuleRef', rid)
        if tid == 0x06:
            return self.method_full_name(rid)
        if tid == 0x1B:
            return self.typespec_name(rid)
        return f'?parent{tid:#x}!{rid}'

    def resolve_token(self, tok):
        md = self.md
        tab = tok >> 24
        rid = tok & 0xFFFFFF
        try:
            if tab == 0x0A:  # MemberRef
                tid, r = md.get('MemberRef', rid, 'Class')
                return f'{self._parent_name(tid, r)}::{md.name("MemberRef", rid)}'
            if tab == 0x06:
                return self.method_full_name(rid)
            if tab == 0x04:
                return self.field_full_name(rid)
            if tab == 0x02:
                return self.full_type.get(rid, f'TypeDef!{rid}')
            if tab == 0x01:
                return self.type_ref_name(rid)
            if tab == 0x1B:
                return self.typespec_name(rid)
            if tab == 0x2B:  # MethodSpec
                mtid, mrid = md.get('MethodSpec', rid, 'Method')
                gen = self.resolve_token((mtid << 24) | mrid) if mtid else '?'
                try:
                    b = self.blob(md.get('MethodSpec', rid, 'Instantiation'))
                    p = 0
                    cnt, p = self._compr(b, p)
                    args = []
                    for _ in range(cnt):
                        s, p = self._sig_type(b, p)
                        args.append(s)
                    return f"{gen}<{','.join(args)}>"
                except Exception:
                    return gen + '<...>'
            if tab == 0x70:
                return '"' + self.user_string(rid) + '"'
            if tab == 0x1A:
                return 'Method!rid' + str(rid)
        except Exception as e:
            return f'<tok {tok:#x} err {e}>'
        return f'<tok {tok:#010x} tab={tab:#x} rid={rid}>'

    # ---------------- тело метода ----------------
    def method_body(self, mrid):
        rva = self.md.get('MethodDef', mrid, 'Rva')
        if not rva:
            return None
        off = self.rva2off(rva)
        d = self.data
        b = d[off]
        if (b & 3) == 0x2:
            return dict(hdr=off, code=off + 1, size=b >> 2, maxstack=8, locsig=None,
                        fat=False, initlocals=False, extra=None)
        if (b & 3) == 0x3:
            flags = struct.unpack_from('<H', d, off)[0]
            hsz = (flags >> 12) * 4
            maxstack = struct.unpack_from('<H', d, off + 2)[0]
            code_size = struct.unpack_from('<I', d, off + 4)[0]
            locsig = struct.unpack_from('<I', d, off + 8)[0]
            extra = None
            if flags & 0x08:  # MoreSects
                p = off + hsz + code_size
                p = (p + 3) & ~3
                extra = self._parse_ex_sects(p)
            return dict(hdr=off, code=off + hsz, size=code_size, maxstack=maxstack,
                        locsig=locsig, fat=True, initlocals=bool(flags & 0x10),
                        extra=extra)
        return None

    def _parse_ex_sects(self, p):
        d = self.data
        clauses = []
        while True:
            kind = d[p]
            if kind & 0x40:  # fat
                dsz = d[p + 1] | (d[p + 2] << 8) | (d[p + 3] << 16)
                p += 4
                n = (dsz - 4) // 24
                for i in range(n):
                    f, to, tl_, ho, hl, ct, ft = struct.unpack_from('<IIIIIII', d, p)
                    clauses.append((f, to, tl_, ho, hl, ct))
                    p += 24
            else:
                dsz = d[p + 1]
                p += 4
                n = (dsz - 4) // 12
                for i in range(n):
                    f = d[p]; to = d[p + 1]; tl_ = d[p + 2]
                    ho, hl, ct = struct.unpack_from('<III', d, p + 3)
                    clauses.append((f, to, tl_, ho, hl, ct))
                    p += 12
            if not (kind & 0x80):
                break
            p = (p + 3) & ~3
        return clauses

    FLAGNAME = {0: 'catch', 1: 'filter', 2: 'finally', 4: 'fault'}

    def disasm(self, mrid, limit=100000):
        mb = self.method_body(mrid)
        if not mb:
            return []
        d = self.data
        p, end = mb['code'], mb['code'] + mb['size']
        base = mb['code']
        out = []
        while p < end and len(out) < limit:
            il = p - base
            b0 = d[p]; p += 1
            if b0 == 0xFE:
                b1 = d[p]; p += 1
                name = OP2.get(b1, f'??FE{b1:02x}')
            else:
                name = OP1.get(b0, f'??{b0:02x}')
            arg = ''
            if name in TOK_OPS:
                tok = struct.unpack_from('<I', d, p)[0]
                arg = self.resolve_token(tok)
                p += 4
            elif name in BR_OPS:
                sz = BR_OPS[name]
                delta = struct.unpack_from('<i' if sz == 4 else '<b', d, p)[0]
                arg = f'IL_{p + sz + delta - base:04x}'
                p += sz
            elif name == 'switch':
                n = struct.unpack_from('<I', d, p)[0]; p += 4
                tbl = list(struct.unpack_from('<' + 'i' * n, d, p)); p += 4 * n
                nxt = p - base
                arg = '(' + ', '.join(f'IL_{nxt + t:04x}' for t in tbl) + ')'
            elif name in SHORT_OPS:
                v = struct.unpack_from('<b' if name == 'ldc.i4.s' else '<B', d, p)[0]
                arg = str(v); p += 1
            elif name in SHORT2_OPS:
                arg = str(struct.unpack_from('<H', d, p)[0]); p += 2
            elif name == 'ldc.i4':
                arg = str(struct.unpack_from('<i', d, p)[0]); p += 4
            elif name == 'ldc.i8':
                arg = str(struct.unpack_from('<q', d, p)[0]); p += 8
            elif name == 'ldc.r4':
                arg = repr(struct.unpack_from('<f', d, p)[0]); p += 4
            elif name == 'ldc.r8':
                arg = repr(struct.unpack_from('<d', d, p)[0]); p += 8
            elif name in ('volatile.', 'tail.', 'readonly.', 'unaligned.', 'endfilter',
                          'rethrow', 'endfinally'):
                if name == 'unaligned.':
                    arg = str(d[p]); p += 1
            out.append((il, name, arg))
        return out

    def handlers(self, mrid):
        mb = self.method_body(mrid)
        if not mb or not mb['extra']:
            return []
        res = []
        for f, to, tl_, ho, hl, ct in mb['extra']:
            res.append(f"  {self.FLAGNAME.get(f & 7, f)}: try IL_{to:04x}..IL_{to + tl_:04x} "
                       f"-> handler IL_{ho:04x}..IL_{ho + hl:04x}"
                       + (f" filter IL_{ct:04x}" if (f & 7) == 1 else ''))
        return res
