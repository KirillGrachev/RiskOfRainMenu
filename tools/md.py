"""
md.py — лёгкий ридер метаданных ECMA-335 для managed-сборок Unity.

Зачем он нужен, если есть dnfile: dnfile при разборе RoR2.dll (7,7 МБ,
47406 методов) занимает ~580 МБ и ~9 секунд, потому что материализует объект
на каждую строку каждой таблицы и на каждую ссылку в MethodList/FieldList.
Здесь таблицы читаются как сырые байты по схемам ECMA-335, а строки
конструируются только по запросу. Результат: ~40 МБ и доли секунды.

Наружу отдаётся API, совместимый с тем, к которому привыкли r2.py/dump.py:

    md = MetaData('RoR2.dll')
    md.count('MethodDef')            # количество строк
    md.get('MethodDef', 33987, 'Rva')          # поле строки (целое)
    md.name('MethodDef', 33987)                # строковое поле Name
    md.string(idx) / md.blob(idx) / md.guid(idx)
    md.tables                        # {имя: Table}
    md.rva_to_off(rva)

Схемы таблиц — ECMA-335 Partition II, пункты 22.1..22.41.
"""
import struct

import pefile

# ---------------------------------------------------------------------------
# строки heap-индексов зависят от размера кучи: 1/2/4 байта
HEAP_STR, HEAP_BLOB, HEAP_GUID = '#Strings', '#Blob', '#GUID'

# coded index: имя -> (число бит тега, {тег: id таблицы})
CODED = {
    'TypeDefOrRef': (2, {0: 0x02, 1: 0x01, 2: 0x1B}),
    'HasConstant': (2, {0: 0x04, 1: 0x08, 2: 0x17}),
    'HasCustomAttribute': (5, {0: 0x06, 1: 0x04, 2: 0x01, 3: 0x02, 4: 0x08, 5: 0x09,
                               6: 0x0A, 7: 0x00, 8: 0x0E, 9: 0x11, 10: 0x14, 11: 0x17,
                               12: 0x12, 13: 0x15, 14: 0x23, 15: 0x20, 16: 0x26,
                               17: 0x27, 18: 0x28, 19: 0x2A, 20: 0x2B, 21: 0x2C,
                               22: 0x2D, 23: 0x2E}),
    'HasFieldMarshal': (1, {0: 0x08, 1: 0x04}),
    'HasDeclSecurity': (2, {0: 0x02, 1: 0x06, 2: 0x20}),
    'MemberRefParent': (3, {0: 0x02, 1: 0x01, 2: 0x1A, 3: 0x06, 4: 0x1B}),
    'HasSemantic': (1, {0: 0x14, 1: 0x17}),
    'MethodDefOrRef': (1, {0: 0x06, 1: 0x0A}),
    'MemberForwarded': (1, {0: 0x04, 1: 0x06}),
    'Implementation': (2, {0: 0x26, 1: 0x23, 2: 0x27}),
    'CustomAttributeType': (3, {2: 0x06, 3: 0x0A, 4: 0x2B}),
    'Resolution': (2, {0: 0x00, 1: 0x1A, 2: 0x23}),
    'TypeOrMethodDef': (1, {0: 0x02, 1: 0x06}),
}

# схемы таблиц: id -> (имя, [(поле, тип), ...])
# типы: 'u1','u2','u4','u8','i4','str','blob','guid', ('idx', table_id), ('coded', name)
SCHEMA = {
    0x00: ('Module', [('Generation', 'u2'), ('Name', 'str'), ('Mvid', 'guid'),
                      ('EncId', 'guid'), ('EncBaseId', 'guid')]),
    0x01: ('TypeRef', [('Resolution', ('coded', 'Resolution')), ('TypeName', 'str'),
                       ('TypeNamespace', 'str')]),
    0x02: ('TypeDef', [('Flags', 'u4'), ('TypeName', 'str'), ('TypeNamespace', 'str'),
                       ('Extends', ('coded', 'TypeDefOrRef')),
                       ('FieldList', ('idx', 0x04)), ('MethodList', ('idx', 0x06))]),
    0x04: ('Field', [('Flags', 'u2'), ('Name', 'str'), ('Signature', 'blob')]),
    0x06: ('MethodDef', [('Rva', 'u4'), ('ImplFlags', 'u2'), ('Flags', 'u2'),
                         ('Name', 'str'), ('Signature', 'blob'),
                         ('ParamList', ('idx', 0x08))]),
    0x08: ('Param', [('Flags', 'u2'), ('Sequence', 'u2'), ('Name', 'str')]),
    0x09: ('InterfaceImpl', [('Class', ('idx', 0x02)),
                             ('Interface', ('coded', 'TypeDefOrRef'))]),
    0x0A: ('MemberRef', [('Class', ('coded', 'MemberRefParent')), ('Name', 'str'),
                         ('Signature', 'blob')]),
    0x0B: ('Constant', [('Type', 'u1'), ('Pad', 'u1'),
                        ('Parent', ('coded', 'HasConstant')), ('Value', 'blob')]),
    0x0C: ('CustomAttribute', [('Parent', ('coded', 'HasCustomAttribute')),
                               ('Type', ('coded', 'CustomAttributeType')),
                               ('Value', 'blob')]),
    0x0D: ('FieldMarshal', [('Parent', ('coded', 'HasFieldMarshal')),
                            ('NativeType', 'blob')]),
    0x0E: ('DeclSecurity', [('Action', 'u2'), ('Parent', ('coded', 'HasDeclSecurity')),
                            ('PermissionSet', 'blob')]),
    0x0F: ('ClassLayout', [('PackingSize', 'u2'), ('ClassSize', 'u4'),
                           ('Parent', ('idx', 0x02))]),
    0x10: ('FieldLayout', [('Offset', 'u4'), ('Field', ('idx', 0x04))]),
    0x11: ('StandAloneSig', [('Signature', 'blob')]),
    0x12: ('EventMap', [('Parent', ('idx', 0x02)), ('EventList', ('idx', 0x14))]),
    0x14: ('Event', [('EventFlags', 'u2'), ('Name', 'str'),
                     ('EventType', ('coded', 'TypeDefOrRef'))]),
    0x15: ('PropertyMap', [('Parent', ('idx', 0x02)),
                           ('PropertyList', ('idx', 0x17))]),
    0x17: ('Property', [('Flags', 'u2'), ('Name', 'str'), ('Type', 'blob')]),
    0x18: ('MethodSemantics', [('Semantics', 'u2'), ('Method', ('idx', 0x06)),
                               ('Association', ('coded', 'HasSemantic'))]),
    0x19: ('MethodImpl', [('Class', ('idx', 0x02)),
                          ('MethodBody', ('coded', 'MethodDefOrRef')),
                          ('MethodDeclaration', ('coded', 'MethodDefOrRef'))]),
    0x1A: ('ModuleRef', [('Name', 'str')]),
    0x1B: ('TypeSpec', [('Signature', 'blob')]),
    0x1C: ('ImplMap', [('MappingFlags', 'u2'),
                       ('MemberForwarded', ('coded', 'MemberForwarded')),
                       ('ImportName', 'str'), ('ImportScope', ('idx', 0x1A))]),
    0x1D: ('FieldRVA', [('Rva', 'u4'), ('Field', ('idx', 0x04))]),
    0x20: ('Assembly', [('HashAlgId', 'u4'), ('MajorVersion', 'u2'),
                        ('MinorVersion', 'u2'), ('BuildNumber', 'u2'),
                        ('RevisionNumber', 'u2'), ('Flags', 'u4'),
                        ('PublicKey', 'blob'), ('Name', 'str'), ('Culture', 'str')]),
    0x21: ('AssemblyProcessor', [('Processor', 'u4')]),
    0x22: ('AssemblyOS', [('OSPlatformId', 'u4'), ('OSMajorVersion', 'u4'),
                          ('OSMinorVersion', 'u4')]),
    0x23: ('AssemblyRef', [('MajorVersion', 'u2'), ('MinorVersion', 'u2'),
                           ('BuildNumber', 'u2'), ('RevisionNumber', 'u2'),
                           ('Flags', 'u4'), ('PublicKeyOrToken', 'blob'),
                           ('Name', 'str'), ('Culture', 'str'), ('HashValue', 'blob')]),
    0x24: ('AssemblyRefProcessor', [('Processor', 'u4'), ('AssemblyRef', ('idx', 0x23))]),
    0x25: ('AssemblyRefOS', [('OSPlatformId', 'u4'), ('OSMajorVersion', 'u4'),
                             ('OSMinorVersion', 'u4'), ('AssemblyRef', ('idx', 0x23))]),
    0x26: ('File', [('Flags', 'u4'), ('Name', 'str'), ('HashValue', 'blob')]),
    0x27: ('ExportedType', [('Flags', 'u4'), ('TypeDefId', 'u4'), ('TypeName', 'str'),
                            ('TypeNamespace', 'str'),
                            ('Implementation', ('coded', 'Implementation'))]),
    0x28: ('ManifestResource', [('Offset', 'u4'), ('Flags', 'u4'), ('Name', 'str'),
                                ('Implementation', ('coded', 'Implementation'))]),
    0x29: ('NestedClass', [('NestedClass', ('idx', 0x02)),
                           ('EnclosingClass', ('idx', 0x02))]),
    0x2A: ('GenericParam', [('Number', 'u2'), ('Flags', 'u2'),
                            ('Owner', ('coded', 'TypeOrMethodDef')), ('Name', 'str')]),
    0x2B: ('MethodSpec', [('Method', ('coded', 'MethodDefOrRef')),
                          ('Instantiation', 'blob')]),
    0x2C: ('GenericParamConstraint', [('Owner', ('idx', 0x2A)),
                                      ('Constraint', ('coded', 'TypeDefOrRef'))]),
}

_ID2NAME = {i: n for i, (n, _) in SCHEMA.items()}
NAME2ID = {n: i for i, (n, _) in SCHEMA.items()}

_FIXED = {'u1': 1, 'u2': 2, 'u4': 4, 'u8': 8, 'i4': 4}


class MdError(Exception):
    pass


class Table:
    __slots__ = ('tid', 'name', 'fields', 'rows', 'row_size', 'offset', 'md',
                 '_layout', 'field_map')

    def __init__(self, md, tid, name, fields, rows, offset):
        self.md = md
        self.tid = tid
        self.name = name
        self.fields = fields
        self.rows = rows
        self.offset = offset
        self._layout = None
        self.row_size = None
        self.field_map = {}

    def __len__(self):
        return self.rows

    def __repr__(self):
        return f'<Table {self.name} rows={self.rows} row_size={self.row_size}>'


class MetaData:
    def __init__(self, path):
        self.path = path
        self.pe = pefile.PE(path, fast_load=True)
        self.pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR']])
        self.data = open(path, 'rb').read()
        self.sections = [(s.VirtualAddress, s.Misc_VirtualSize, s.SizeOfRawData,
                          s.PointerToRawData, s.Name.decode('latin1').rstrip('\0'))
                         for s in self.pe.sections]
        self._parse_cor()
        self._parse_metadata()
        self._parse_tables()

    # ------------------------------------------------------------ PE / CLR --
    def rva_to_off(self, rva):
        for va, vsz, rsz, praw, _n in self.sections:
            if va <= rva < va + max(vsz, rsz):
                return praw + (rva - va)
        raise MdError(f'RVA {rva:#x} вне секций')

    def _parse_cor(self):
        dd = self.pe.OPTIONAL_HEADER.DATA_DIRECTORY[14]
        if not dd.VirtualAddress:
            raise MdError('это не managed-сборка (нет COR20-заголовка)')
        off = self.rva_to_off(dd.VirtualAddress)
        self.cor_off = off
        (self.cor_cb, self.cor_major, self.cor_minor, self.md_rva, self.md_size,
         self.cor_flags, self.cor_entrypoint) = struct.unpack_from('<IHHIIII',
                                                                  self.data, off)
        f = self.cor_flags
        self.cor = dict(
            ilonly=bool(f & 0x1), native_entrypoint=bool(f & 0x10),
            track_debug=bool(f & 0x10000), strongnamesigned=bool(f & 0x8),
            _32bitrequired=bool(f & 0x2), flags=f,
        )
        # остальные пары RVA/Size из COR20 (CodeManagerTable и пр.)
        extra = struct.unpack_from('<' + 'I' * 12, self.data, off + 24)
        names = ['Resources_RVA', 'Resources_Size', 'StrongName_RVA', 'StrongName_Size',
                 'CodeManagerTable_RVA', 'CodeManagerTable_Size', 'VTableFixups_RVA',
                 'VTableFixups_Size', 'ExportAddressTableJumps_RVA',
                 'ExportAddressTableJumps_Size', 'ManagedNativeHeader_RVA',
                 'ManagedNativeHeader_Size']
        self.cor.update(dict(zip(names, extra)))

    # ------------------------------------------------------------- streams --
    def _parse_metadata(self):
        d = self.data
        self.md_off = self.rva_to_off(self.md_rva)
        p = self.md_off
        sig, = struct.unpack_from('<I', d, p)
        if sig != 0x424A5342:
            raise MdError(f'сигнатура метаданных {sig:#x} != 0x424A5342')
        self.md_major, self.md_minor = struct.unpack_from('<HH', d, p + 4)
        self.md_reserved, = struct.unpack_from('<I', d, p + 8)
        vlen, = struct.unpack_from('<I', d, p + 12)
        self.runtime_version = d[p + 16:p + 16 + vlen].rstrip(b'\0').decode('latin1')
        q = p + 16 + vlen
        self.md_flags, nstreams = struct.unpack_from('<HH', d, q)
        q += 4
        self.streams = {}
        for _ in range(nstreams):
            srva, ssize = struct.unpack_from('<II', d, q)
            q += 8
            e = d.index(b'\0', q)
            nm = d[q:e].decode('latin1')
            q = (e + 1 + 3) & ~3
            # Offset в заголовке потока отсчитывается ОТ КОРНЯ метаданных,
            # а не от начала образа — поэтому просто md_off + srva.
            self.streams[nm] = (self.md_off + srva, ssize)
        if '#~' in self.streams:
            self.tables_stream = '#~'
        elif '#-' in self.streams:
            self.tables_stream = '#-'          # uncompressed / edit-and-continue
        else:
            raise MdError('нет потока таблиц (#~ / #-)')

    # -------------------------------------------------------------- tables --
    def _heap_index_size(self, name):
        off, size = self.streams.get(name, (0, 0))
        return 4 if size >= 0x10000 else 2

    def _parse_tables(self):
        d = self.data
        toff, tsize = self.streams[self.tables_stream]
        reserved, = struct.unpack_from('<I', d, toff)
        if reserved != 0:
            raise MdError(f'#~ reserved != 0 ({reserved:#x}) — поток повреждён?')
        major, minor, heapsizes, reserved2 = struct.unpack_from('<BBBB', d, toff + 4)
        valid, sorted_ = struct.unpack_from('<QQ', d, toff + 8)
        self.heap_sizes = heapsizes
        self.str_size = 4 if heapsizes & 1 else 2
        self.guid_size = 4 if heapsizes & 2 else 2
        self.blob_size = 4 if heapsizes & 4 else 2
        self.tables_valid = valid
        self.tables_sorted = sorted_

        p = toff + 24
        present = [t for t in range(64) if (valid >> t) & 1]
        counts = {}
        for t in present:
            counts[t], = struct.unpack_from('<I', d, p)
            p += 4
        self.tables_start = p

        # размеры индексов
        self.counts = counts

        def idx_size(tid):
            return 4 if counts.get(tid, 0) >= 0x10000 else 2

        def coded_size(name):
            bits, tags = CODED[name]
            mx = max((counts.get(t, 0) for t in tags.values()), default=0)
            return 4 if mx >= (1 << (16 - bits)) else 2

        self.tables = {}
        cur = p
        for t in present:
            if t not in SCHEMA:
                raise MdError(f'таблица {t:#x} неизвестна схеме — парсер устарел')
            name, fields = SCHEMA[t]
            tb = Table(self, t, name, fields, counts[t], cur)
            # раскладка полей
            layout = []
            off = 0
            for fname, ftype in fields:
                if ftype in _FIXED:
                    sz = _FIXED[ftype]
                elif ftype == 'str':
                    sz = self.str_size
                elif ftype == 'blob':
                    sz = self.blob_size
                elif ftype == 'guid':
                    sz = self.guid_size
                elif isinstance(ftype, tuple) and ftype[0] == 'idx':
                    sz = idx_size(ftype[1])
                elif isinstance(ftype, tuple) and ftype[0] == 'coded':
                    sz = coded_size(ftype[1])
                else:
                    raise MdError(f'неизвестный тип поля {ftype}')
                layout.append((fname, ftype, off, sz))
                off += sz
            tb._layout = layout
            tb.field_map = {fname: (ftype, foff, fsz) for fname, ftype, foff, fsz in layout}
            tb.row_size = off
            cur += off * counts[t]
            self.tables[name] = tb
        self.tables_end = cur

    # ------------------------------------------------------------- доступ ---
    def table(self, name):
        if name not in self.tables:
            raise MdError(f'в этой сборке нет таблицы {name}')
        return self.tables[name]

    def count(self, name):
        return self.tables[name].rows if name in self.tables else 0

    _FMT = {'u1': '<B', 'u2': '<H', 'u4': '<I', 'u8': '<Q', 'i4': '<i'}

    def field_type(self, table_name, field):
        try:
            return self.tables[table_name].field_map[field][0]
        except KeyError:
            raise MdError(f'в таблице {table_name} нет поля {field}')

    def get(self, table_name, rid, field):
        """Значение поля строки. rid — от 1."""
        tb = self.tables[table_name]
        if not 1 <= rid <= tb.rows:
            raise MdError(f'{table_name} rid {rid} вне диапазона 1..{tb.rows}')
        try:
            ftype, off, sz = tb.field_map[field]
        except KeyError:
            raise MdError(f'в таблице {table_name} нет поля {field} '
                          f'(есть: {", ".join(tb.field_map)})')
        p = tb.offset + (rid - 1) * tb.row_size + off
        d = self.data
        if ftype in _FIXED:
            return struct.unpack_from(self._FMT[ftype], d, p)[0]
        if ftype in ('str', 'blob', 'guid'):
            return struct.unpack_from('<I' if sz == 4 else '<H', d, p)[0]
        if isinstance(ftype, tuple):
            raw = struct.unpack_from('<I' if sz == 4 else '<H', d, p)[0]
            if ftype[0] == 'idx':
                return raw
            bits, tags = CODED[ftype[1]]
            return (tags.get(raw & ((1 << bits) - 1), 0), raw >> bits)
        raise MdError(f'не умею читать поле {field}')

    def row(self, table_name, rid):
        """Все поля строки сразу (dict)."""
        tb = self.tables[table_name]
        return {f[0]: self.get(table_name, rid, f[0]) for f in tb._layout}

    def name(self, table_name, rid, field='Name'):
        """Строковое поле как str (типично Name / TypeName)."""
        return self.string(self.get(table_name, rid, field))

    # --------------------------------------------------------------- heaps --
    def _heap(self, name):
        return self.streams[name]

    def string(self, idx):
        if not idx:
            return ''
        off, size = self._heap(HEAP_STR)
        d = self.data
        e = d.index(b'\0', off + idx)
        return d[off + idx:e].decode('utf-8', 'replace')

    def blob(self, idx):
        if not idx:
            return b''
        off, size = self._heap(HEAP_BLOB)
        d = self.data
        p = off + idx
        b0 = d[p]
        if b0 < 0x80:
            n, p = b0, p + 1
        elif b0 < 0xC0:
            n, p = ((b0 & 0x3F) << 8) | d[p + 1], p + 2
        else:
            n = ((b0 & 0x1F) << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3]
            p += 4
        return d[p:p + n]

    def guid(self, idx):
        if not idx:
            return ''
        off, size = self._heap(HEAP_GUID)
        p = off + (idx - 1) * 16
        a, b, c = struct.unpack_from('<IHH', self.data, p)
        rest = self.data[p + 8:p + 16]
        return f'{a:08x}-{b:04x}-{c:04x}-{rest[0]:02x}{rest[1]:02x}-' \
               + ''.join(f'{x:02x}' for x in rest[2:])

    def user_string(self, idx):
        off, size = self._heap('#US')
        d = self.data
        p = off + idx
        b0 = d[p]
        if b0 < 0x80:
            n, p = b0, p + 1
        elif b0 < 0xC0:
            n, p = ((b0 & 0x3F) << 8) | d[p + 1], p + 2
        else:
            n = ((b0 & 0x1F) << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3]
            p += 4
        return d[p:p + max(n - 1, 0)].decode('utf-16-le', 'replace')

    # --------------------------------------------------------- conveniences -
    def table_id_name(self, tid):
        return _ID2NAME.get(tid, f'Table{tid:#x}')

    def typedef_method_range(self, tid):
        """(первый RID, количество) методов TypeDef — по ECMA методы типа
        лежат в MethodDef сплошным блоком."""
        first = self.get('TypeDef', tid, 'MethodList')
        nxt = self.get('TypeDef', tid + 1, 'MethodList') if tid < self.count('TypeDef') \
            else self.count('MethodDef') + 1
        return first, max(0, nxt - first)

    def typedef_field_range(self, tid):
        first = self.get('TypeDef', tid, 'FieldList')
        nxt = self.get('TypeDef', tid + 1, 'FieldList') if tid < self.count('TypeDef') \
            else self.count('Field') + 1
        return first, max(0, nxt - first)

    def list_range(self, owner_table, owner_rid, field, target_table):
        """(первый RID, количество) для полей-списков вроде TypeDef.MethodList.
        По ECMA строки владельца лежат сплошным блоком, поэтому граница —
        начало блока у следующей строки владельца."""
        first = self.get(owner_table, owner_rid, field)
        if owner_rid < self.count(owner_table):
            nxt = self.get(owner_table, owner_rid + 1, field)
        else:
            nxt = self.count(target_table) + 1
        return first, max(0, nxt - first)

    def owner_of_method(self, mrid):
        """RID TypeDef, которому принадлежит метод. Бинарный поиск по
        начальным RID блоков — O(log n) без построения карты на 47k записей."""
        return self._owner(mrid, 'MethodList', 'MethodDef')

    def owner_of_field(self, frid):
        return self._owner(frid, 'FieldList', 'Field')

    def _owner(self, rid, list_field, table_name):
        n = self.count('TypeDef')
        lo, hi = 1, n
        best = 0
        while lo <= hi:
            mid = (lo + hi) // 2
            first = self.get('TypeDef', mid, list_field)
            if first <= rid:
                best = mid
                lo = mid + 1
            else:
                hi = mid - 1
        if not best:
            return 0
        first, cnt = (self.typedef_method_range(best) if list_field == 'MethodList'
                      else self.typedef_field_range(best))
        return best if first <= rid < first + cnt else 0
