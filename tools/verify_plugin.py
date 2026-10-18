"""
verify_plugin.py — проверка собранного плагина без запуска игры.

Компиляция доказывает, что имена членов существуют. Но она не доказывает двух
вещей, из-за которых плагин может молча не работать в игре:

  1. внутрь сборки не должны попасть чужие типы. Дефолтный glob MSBuild однажды
     вкомпилировал stubs/HlapiStub.cs прямо в плагин, и UnityEngine.Networking.
     NetworkServer.active стал нашим стабом, который всегда возвращает false —
     плагин собирался, грузился и просто ничего не делал. Проверяем, что в
     плагине нет TypeDef'ов из чужих пространств имён и что hlapi присутствует
     именно как AssemblyRef.

  2. цели Harmony-патчей должны существовать в той RoR2.dll, с которой собран
     плагин. Harmony при промахе пишет warning в лог и продолжает без патча, то
     есть разблокировки тихо перестают работать. Проверяем по метаданным, что
     каждый пропатченный метод есть в сборке и сигнатура совпадает.

Запуск:
    python3 tools/verify_plugin.py release/RoR2SandboxMenu.dll [--dll RoR2.dll]
"""
import argparse
import hashlib
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from md import MetaData                     # noqa: E402
from r2 import Asm                          # noqa: E402
from sigres import decode_method_sig        # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

#: ссылки, которые обязаны быть у плагина (имя -> минимальная мажорная версия)
REQUIRED_REFS = {
    'RoR2': None,
    'BepInEx': 5,
    '0Harmony': 2,
    'netstandard': 2,
    'com.unity.multiplayer-hlapi.Runtime': 1,
}

#: чужие пространства имён, которых внутри плагина быть не должно
FORBIDDEN_TYPEDEFS = ('UnityEngine.Networking', 'HarmonyLib', 'BepInEx', 'RoR2')

#: (тип, метод, параметры) — цели Harmony-патчей из src/UnlockPatches.cs
HARMONY_TARGETS = [
    ('RoR2.UserProfile', 'HasUnlockable', ['RoR2.UnlockableDef']),
    ('RoR2.UserProfile', 'HasUnlockable', ['System.String']),
    ('RoR2.UserProfile', 'HasSurvivorUnlocked', ['RoR2.SurvivorIndex']),
    ('RoR2.UserProfile', 'HasViewedViewable', ['System.String']),
]

PASS, FAIL = [], []


def check(name, ok, detail=''):
    (PASS if ok else FAIL).append(name)
    print(f'  [{"PASS" if ok else "FAIL"}] {name}' + (f'  — {detail}' if detail else ''))
    return ok


def norm(t):
    """System.String -> string, RoR2.UnlockableDef -> короткое имя."""
    if t in ('System.String', 'string'):
        return 'string'
    return t.rsplit('.', 1)[-1] if '.' in t else t


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('plugin')
    ap.add_argument('--dll', default=os.path.join(ROOT, 'RoR2.dll'),
                    help='RoR2.dll, с которой должен быть собран плагин')
    args = ap.parse_args()

    path = args.plugin
    if not os.path.isabs(path):
        cand = os.path.join(ROOT, path)
        path = cand if os.path.exists(cand) else path
    raw = open(path, 'rb').read()

    print(f'=== {os.path.basename(path)} ===')
    print(f'  размер : {len(raw)} байт')
    print(f'  sha256 : {hashlib.sha256(raw).hexdigest()[:24]}')

    md = MetaData(path)
    a = Asm(path)

    print('\n1. идентичность сборки')
    check('ILONLY (управляемый IL, без нативной части)', bool(md.cor['ilonly']),
          f"flags={md.cor['flags']:#x}")
    check('без strong name (иначе конфликт с игровой сборкой)',
          md.cor['StrongName_Size'] == 0)
    check('runtime v4.0.30319', md.runtime_version == 'v4.0.30319', md.runtime_version)

    print('\n2. ссылки на сборки')
    refs = {}
    for i in range(1, md.count('AssemblyRef') + 1):
        refs[md.name('AssemblyRef', i)] = md.get('AssemblyRef', i, 'MajorVersion')
    for name, minver in REQUIRED_REFS.items():
        ok = name in refs
        detail = f'v{refs[name]}' if ok else 'ОТСУТСТВУЕТ'
        if ok and minver is not None:
            ok = refs[name] >= minver
            detail += f' (нужно >= {minver})'
        check(f'ссылка {name}', ok, detail)
    extra = sorted(set(refs) - set(REQUIRED_REFS))
    print(f'       прочие ссылки: {extra}')

    print('\n3. внутрь не попали чужие типы')
    own = []
    for i in range(1, md.count('TypeDef') + 1):
        ns = md.name('TypeDef', i, 'TypeNamespace')
        if any(ns == f or ns.startswith(f + '.') for f in FORBIDDEN_TYPEDEFS):
            own.append(f'{ns}.{md.name("TypeDef", i, "TypeName")}')
    check('нет TypeDef из чужих пространств имён', not own,
          '; '.join(own[:4]) if own else 'только SandboxMenu.*')
    mine = [a.full_type.get(i) for i in range(1, md.count('TypeDef') + 1)
            if md.name('TypeDef', i, 'TypeNamespace') == 'SandboxMenu']
    print(f'       своих типов SandboxMenu.*: {len(mine)}')

    print('\n4. точка входа BepInEx')
    plugin_types = []
    harmony_types = []
    for i in range(1, md.count('CustomAttribute') + 1):
        par = md.get('CustomAttribute', i, 'Parent')
        if par[0] != 0x02:
            continue
        t = md.get('CustomAttribute', i, 'Type')
        nm = ''
        try:
            if t[0] == 0x0A:
                nm = md.name('MemberRef', t[1], 'Name')
                cls = md.get('MemberRef', t[1], 'Class')
                if cls[0] == 0x01:
                    nm = md.name('TypeRef', cls[1], 'TypeName')
            elif t[0] == 0x06:
                owner = md.owner_of_method(t[1])
                nm = md.name('TypeDef', owner, 'TypeName')
        except Exception:
            pass
        # в BepInEx 5 класс атрибута называется BepInPlugin, без суффикса Attribute
        if nm in ('BepInPlugin', 'BepInPluginAttribute'):
            plugin_types.append(a.full_type.get(par[1], '?'))
        elif nm in ('HarmonyPatch', 'HarmonyPatchAttribute'):
            harmony_types.append(a.full_type.get(par[1], '?'))
    check('есть ровно один тип с [BepInPlugin]', len(plugin_types) == 1, str(plugin_types))

    check('есть типы с [HarmonyPatch]', len(harmony_types) == len(HARMONY_TARGETS),
          f'{len(harmony_types)} шт.: {[t.split(".")[-1] for t in harmony_types]}')

    print('\n5. цели Harmony-патчей существуют в RoR2.dll')
    if not os.path.exists(args.dll):
        print(f'       [SKIP] нет {args.dll}: сборка игры в репозиторий не входит. '
              f'Укажите --dll <путь к вашей RoR2.dll>, чтобы проверить цели патчей.')
        print('\n' + '=' * 68)
        print(f'ПРОШЛО: {len(PASS)}   ПРОВАЛЕНО: {len(FAIL)}   ПРОПУЩЕНО: 1')
        for f in FAIL:
            print(f'  - {f}')
        print('=' * 68)
        return 1 if FAIL else 0
    game = Asm(args.dll)
    gmd = game.md
    gsha = hashlib.sha256(open(args.dll, 'rb').read()).hexdigest()[:16]
    print(f'       проверяем против {os.path.basename(args.dll)} sha256 {gsha}')
    for tname, mname, params in HARMONY_TARGETS:
        ids = game.by_name.get(tname)
        found = None
        if ids:
            for tid in ids:
                mf, mc = gmd.typedef_method_range(tid)
                for m in range(mf, mf + mc):
                    if gmd.name('MethodDef', m) != mname:
                        continue
                    s = decode_method_sig(game, m)
                    if not s:
                        continue
                    got = [norm(x) for x in s['args']]
                    want = [norm(x) for x in params]
                    if got == want:
                        found = (m, s)
                        break
        args_txt = ', '.join(p.split('.')[-1] for p in params)
        label = f'{tname.split(".")[-1]}.{mname}({args_txt})'
        check(label, found is not None,
              f'RID {found[0]}, возврат {found[1]["ret"]}' if found else
              'МЕТОД НЕ НАЙДЕН — патч молча не применится')

    print('\n' + '=' * 68)
    print(f'ПРОШЛО: {len(PASS)}   ПРОВАЛЕНО: {len(FAIL)}')
    for f in FAIL:
        print(f'  - {f}')
    print('=' * 68)
    return 1 if FAIL else 0


if __name__ == '__main__':
    raise SystemExit(main())
