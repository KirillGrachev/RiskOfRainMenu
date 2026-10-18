"""
sigres.py — привязка патча к СИГНАТУРАМ, а не к байтовым смещениям.

Зачем: старый ilpatch.py держал цели как (RID метода, ожидаемый размер IL).
RID — это просто порядковый номер строки в таблице MethodDef конкретной сборки.
После любого обновления игры разработчики добавляют/удаляют типы и методы,
нумерация едет, и RID 33987 начинает указывать совсем не туда. Размер IL
(13 байт) — такая же привязка к сборке.

Здесь цель описывается тем, что НЕ меняется между сборками:

  1. полным именем типа          RoR2.UserProfile
  2. именем метода              HasUnlockable
  3. сигнатурой                 (RoR2.UnlockableDef) -> boolean
  4. IL-отпечатком метода       ldfld statSheet; callvirt StatSheet::HasUnlockable; ret
  5. сборкой-владельцем         RoR2 (а не Assembly-CSharp-фасад)

1-3 дают поиск; 4 — подтверждение, что найденный метод всё ещё делает то,
что мы думаем (защита от случая «имя осталось, логика переписана»);
5 — защита от патча не того файла.

Ничего обфусцированного в RoR2.dll нет (имена полные), поэтому имена — это
и есть самая надёжная сигнатура. Токены (0x06xxxxxx / 0x0Axxxxxx) в паттернах
намеренно не используются: их номера пересчитываются при каждой сборке.
Вместо них резолвер подставляет в отпечаток имена `Тип::член`.
"""
import json
import os
import re
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from r2 import Asm, ELEMENT_TYPE          # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SPEC_PATH = os.path.join(ROOT, 'sigs.json')
PROFILE_PATH = os.environ.get('SIGPATCH_PROFILE') or \
    os.path.join(ROOT, 'release', 'build_profile.json')

# ---------------------------------------------------------------------------
# сигнатура метода (MethodDefSig из #Blob)
# ---------------------------------------------------------------------------
CC_HAS_THIS = 0x20
CC_EXPLICIT_THIS = 0x40
CC_VARARG = 0x05
CC_GENERIC = 0x10
CC_MASK = 0x0F


def sig_bytes(a, mrid):
    """Сырые байты MethodDefSig из #Blob."""
    s = a.tl.MethodDef.rows[mrid - 1].Signature
    if isinstance(s, (bytes, bytearray)):
        return bytes(s)
    try:
        return a.blob(int(s))
    except Exception:
        return None


def _tdor(a, coded):
    """TypeDefOrRefEncoded -> имя типа."""
    tab = coded & 3
    rid = coded >> 2
    tl = a.tl
    try:
        if tab == 0:
            return a.full_type.get(rid, f'TypeDef!{rid}')
        if tab == 1:
            r = tl.TypeRef.rows[rid - 1]
            ns = str(r.TypeNamespace)
            return f'{ns}.{r.TypeName}' if ns else str(r.TypeName)
        if tab == 2:
            return a.typespec_name(rid)
    except Exception:
        pass
    return f'?tdor{tab}!{rid}'


def _stype(a, b, p):
    """Декодер типа из сигнатуры. От r2.Asm._sig_type отличается тем, что
    TypeDefOrRefEncoded резолвится в имя, а не в номер строки таблицы."""
    if p >= len(b):
        return '?', p
    t = b[p]
    p += 1
    if t in ELEMENT_TYPE:
        return ELEMENT_TYPE[t], p
    if t in (0x11, 0x12):                       # VALUETYPE / CLASS
        n, p = a._compr(b, p)
        return _tdor(a, n), p
    if t == 0x15:                               # GENERICINST
        kind = b[p]
        p += 1
        n, p = a._compr(b, p)
        base = _tdor(a, n)
        argc, p = a._compr(b, p)
        args = []
        for _ in range(argc):
            s, p = _stype(a, b, p)
            args.append(s)
        suffix = '!' if kind == 0x11 else ''
        return f'{base}{suffix}<{",".join(args)}>', p
    if t == 0x13:                               # VAR (тип-параметр типа)
        n, p = a._compr(b, p)
        return f'!{n}', p
    if t == 0x1E:                               # MVAR (тип-параметр метода)
        n, p = a._compr(b, p)
        return f'!!{n}', p
    if t == 0x1D:                               # SZARRAY
        s, p = _stype(a, b, p)
        return f'{s}[]', p
    if t == 0x14:                               # ARRAY
        s, p = _stype(a, b, p)
        rank, p = a._compr(b, p)
        nsz, p = a._compr(b, p)
        for _ in range(nsz):
            _, p = a._compr(b, p)
        nlo, p = a._compr(b, p)
        for _ in range(nlo):
            _, p = a._compr(b, p)
        return (f'{s}[]' if rank == 1 else f'{s}[{",".join([""] * rank)}]'), p
    if t == 0x10:                               # PTR
        s, p = _stype(a, b, p)
        return f'{s}*', p
    if t == 0x0F:                               # BYREF
        s, p = _stype(a, b, p)
        return f'ref {s}', p
    if t == 0x20:                               # CMOD_OPT
        n, p = a._compr(b, p)
        s, p = _stype(a, b, p)
        return s, p
    if t == 0x1F:                               # CMOD_REQ
        n, p = a._compr(b, p)
        s, p = _stype(a, b, p)
        return s, p
    if t == 0x1C:
        return 'object', p
    return f'?{t:02x}', p


def decode_method_sig(a, mrid):
    """{instance, ret, args, generic} для MethodDef — либо None."""
    b = sig_bytes(a, mrid)
    if not b:
        return None
    p = 0
    cc = b[p]
    p += 1
    generic = False
    if cc & CC_GENERIC:
        generic = True
        _, p = a._compr(b, p)                   # GenParamCount
    if (cc & CC_MASK) == CC_VARARG:
        return None                             # vararg — не наш случай
    param_count, p = a._compr(b, p)
    ret, p = _stype(a, b, p)
    args = []
    for _ in range(param_count):
        s, p = _stype(a, b, p)
        args.append(s)
    return dict(instance=bool(cc & CC_HAS_THIS), ret=ret, args=args, generic=generic)


def _short(t):
    """RoR2.UnlockableDef -> UnlockableDef, RoR2.SurvivorIndex -> SurvivorIndex."""
    if not t:
        return t
    return t.rsplit('.', 1)[-1] if '.' in t else t


def _norm_type(t):
    return _short(t).replace(' ', '')


def sig_matches(a, mrid, want):
    """Сверка сигнатуры метода с эталоном из sigs.json.

    want: {'args': [...], 'ret': ..., 'instance': true|false, 'generic': false}
    Пустой/отсутствующий список в эталоне = «не проверяем».
    Имена типов сравниваются по короткому имени: полное имя зависит от того,
    лежит ли тип в этой же сборке (TypeDef) или приходит из ссылки (TypeRef),
    а короткое стабильно.
    """
    r = a.tl.MethodDef.rows[mrid - 1]
    got = decode_method_sig(a, mrid)
    if got is None:
        return False, f'сигнатуру метода `{r.Name}` не удалось разобрать'

    if 'args' in want and want['args'] is not None:
        w = [_norm_type(x) for x in want['args']]
        g = [_norm_type(x) for x in got['args']]
        if w != g:
            return False, f'параметры: ждём {w}, в dll {g}'
    if want.get('ret'):
        if _norm_type(want['ret']) != _norm_type(got['ret']):
            return False, f'возврат: ждём {want["ret"]}, в dll {got["ret"]}'
    if 'instance' in want:
        if bool(want['instance']) != got['instance']:
            return False, f'instance: ждём {want["instance"]}, в dll {got["instance"]}'
    if 'generic' in want:
        if bool(want['generic']) != got['generic']:
            return False, f'generic: ждём {want["generic"]}, в dll {got["generic"]}'
    args_txt = ', '.join(_short(x) for x in got['args'])
    return True, f'({args_txt}) -> {_short(got["ret"])}'


def render_il(a, mrid, limit=400):
    """IL метода строками `опкод операнд` — токены уже заменены именами."""
    return [f'{op}{(" " + arg) if arg else ""}' for _il, op, arg in a.disasm(mrid, limit)]


# ---------------------------------------------------------------------------
# сопоставление IL-отпечатка
# ---------------------------------------------------------------------------
def _item_match(pat, line):
    pat = pat.strip()
    line = line.strip()
    if pat == '*':
        return True
    if pat.startswith('/') and pat.endswith('/') and len(pat) > 2:
        return re.search(pat[1:-1], line) is not None
    if any(ch in pat for ch in '*?'):
        return re.fullmatch(re.escape(pat).replace(r'\*', '.*').replace(r'\?', '.'), line) is not None
    return pat == line


def fingerprint_match(a, mrid, fp):
    """Проверяет IL-отпечаток.

    fp: {'mode': 'subsequence'|'exact'|'contiguous', 'il': [...]}
      subsequence — паттерн должен встретиться как подпоследовательность
                    (между пунктами может быть что угодно). Устойчив к тому,
                    что компилятор добавил/убрал проверки и nop'ы.
      contiguous  — пункты идут подряд, без промежутков.
      exact       — весь IL метода совпадает с паттерном пункт в пункт.
    Возвращает (ok, пояснение, фактические строки IL).
    """
    lines = render_il(a, mrid)
    pat = [p for p in (fp or {}).get('il', [])]
    mode = (fp or {}).get('mode', 'subsequence')
    if not pat:
        return True, 'отпечаток не задан', lines
    if mode == 'exact':
        ok = len(pat) == len(lines) and all(_item_match(p, l) for p, l in zip(pat, lines))
        return ok, f'exact: {len(lines)} инструкций' , lines
    if mode == 'contiguous':
        for i in range(len(lines) - len(pat) + 1):
            if all(_item_match(p, l) for p, l in zip(pat, lines[i:i + len(pat)])):
                return True, f'contiguous: найден на IL_{i:04x}', lines
        return False, 'contiguous: подряд не найден', lines
    # subsequence
    j = 0
    for line in lines:
        if j < len(pat) and _item_match(pat[j], line):
            j += 1
    if j == len(pat):
        return True, f'subsequence: все {len(pat)} пункта найдены', lines
    return False, f'subsequence: совпало {j}/{len(pat)} пунктов', lines


# ---------------------------------------------------------------------------
# резолвер цели
# ---------------------------------------------------------------------------
class ResolveError(Exception):
    pass


def _types_by_name(a, name):
    """TypeDef'ы с таким полным именем (включая вложенные `A+B`)."""
    out = list(a.by_name.get(name, []))
    if out:
        return out
    # допускаем, что в сиге тип написан коротко
    short = name.rsplit('.', 1)[-1]
    return [i for i, n in a.full_type.items() if n == short or n.endswith('+' + short)]


def resolve_target(a, t, skip_fingerprint=False):
    """Возвращает dict(rid=..., detail=..., il=[...], body=..., handlers=[...]).

    Бросает ResolveError с внятной причиной, если цель не найдена или не
    подтверждена отпечатком. НИКОГДА не угадывает: неоднозначность = ошибка.

    skip_fingerprint=True нужен только при верификации УЖЕ пропатченного файла:
    там IL, естественно, больше не совпадает с эталоном — метод ищется по
    имени и сигнатуре, чтобы прочитать получившийся код.
    """
    sig = t['sig']
    tname = sig['type']
    mname = sig['method']

    tids = _types_by_name(a, tname)
    if not tids:
        raise ResolveError(f'тип `{tname}` в сборке не найден (переименован/удалён?)')

    cands = []
    for tid in tids:
        trow = a.tl.TypeDef.rows[tid - 1]
        for m in trow.MethodList:
            if str(m.row.Name) == mname:
                cands.append(m.row_index)
    if not cands:
        raise ResolveError(f'в `{tname}` нет метода `{mname}` (переименован?)')

    # сигнатура
    checked = []
    ok_rids = []
    for rid in cands:
        good, why = sig_matches(a, rid, sig)
        checked.append((rid, good, why))
        if good:
            ok_rids.append(rid)
    if not ok_rids:
        detail = '; '.join(f'[{r}] {w}' for r, g, w in checked if not g)
        raise ResolveError(f'метод `{mname}` найден ({len(cands)} шт.), '
                           f'но сигнатура не та: {detail}')
    if len(ok_rids) > 1:
        raise ResolveError(f'неоднозначность: под сигнатуру подходит {len(ok_rids)} методов '
                           f'{ok_rids} — уточните sig.args')

    rid = ok_rids[0]
    sig_text = next(w for r, g, w in checked if r == rid and g)
    mb = a.method_body(rid)
    if mb is None:
        raise ResolveError(f'[{rid}] {tname}::{mname} не имеет IL-тела (abstract/pinvoke)')

    # отпечаток
    fp = None if skip_fingerprint else t.get('fingerprint')
    ok, why, lines = fingerprint_match(a, rid, fp)
    if not ok:
        raise ResolveError(
            f'[{rid}] {tname}::{mname} найден, но IL-отпечаток не сошёлся ({why}).\n'
            f'    Метод, скорее всего, переписан в новой версии. Сейчас там:\n'
            + '\n'.join(f'      IL_{i:04x}  {l}' for i, l in enumerate(lines[:40])))

    handlers = a.handlers(rid)
    if handlers and not t.get('allow_handlers'):
        raise ResolveError(
            f'[{rid}] у метода {len(handlers)} exc-секций (try/catch/finally). '
            f'Замена тела in place оставит обработчики указывать в мёртвую зону — '
            f'CLR/Mono такую сборку не загрузит. Нужно либо отказаться от цели, '
            f'либо разрешить стратегию code cave (strategy="cave").')

    return dict(rid=rid, tname=tname, mname=mname, why=why, il=lines,
                body=mb, handlers=handlers, sig_text=sig_text)


def load_spec(path=None):
    p = path or SPEC_PATH
    with open(p, 'r', encoding='utf-8') as f:
        return json.load(f)


def load_dll(spec, override=None):
    """Открываем dll и проверяем, что это та сборка, которую ожидает патч."""
    path = override or os.path.join(ROOT, spec['input']['dll'])
    if not os.path.exists(path):
        raise ResolveError(f'нет файла {path}')
    a = Asm(path)

    owner = spec['input'].get('assembly')
    if owner:
        name = None
        if a.md.count('Assembly'):
            name = a.md.name('Assembly', 1)
        if name != owner:
            n_exp = a.md.count('ExportedType')
            n_td = a.md.count('TypeDef')
            hint = ''
            if n_exp > n_td:
                hint = (f'\n    Похоже на фасад-форвардер: {n_exp} ExportedType '
                        f'(пересылок) против {n_td} настоящих типов. Весь игровой '
                        f'код физически лежит в {owner}.dll, а этот файл лишь '
                        f'перенаправляет туда типы — патчить его бессмысленно.')
            refs = sorted(set(a.assembly_ref.values()))[:6]
            raise ResolveError(
                f'это не та сборка: ждём `{owner}`, а открыли `{name}`{hint}\n'
                f'    (в AssemblyRef: {refs}...)')
    return path, a


def build_profile(a, path):
    import hashlib
    d = open(path, 'rb').read()
    tl = a.tl
    return dict(
        file=os.path.basename(path),
        sha256=hashlib.sha256(d).hexdigest(),
        size=len(d),
        mvid=str(tl.Module.rows[0].Mvid),
        typedefs=len(tl.TypeDef.rows),
        methoddefs=len(tl.MethodDef.rows),
        fields=len(tl.Field.rows) if getattr(tl, 'Field', None) else 0,
        assemblyrefs=len(tl.AssemblyRef.rows),
    )
