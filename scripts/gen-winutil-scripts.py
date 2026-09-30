"""Generate Tools/Windows/WinUtilScripts.cs: the WinUtil entries that need more than
registry values — script tweaks, services, optional features, fixes, legacy panels,
DNS providers, Windows Update policies and the Ultimate Performance power plan.

Usage (from the repository root):
    python scripts/gen-winutil-scripts.py <winutil-checkout-or-download-dir> Tools/Windows/WinUtilScripts.cs

The directory must hold config/tweaks.json, config/feature.json, config/dns.json and
functions/public|private/*.ps1 — a clone of https://github.com/ChrisTitusTech/winutil,
or the same files downloaded alongside each other.

WinUtil's scripts are embedded word for word. Entries that download anything from
the internet are left out on purpose — osXos makes no network requests — and so are
entries that duplicate an osXos tool. Both lists are at the top of this file.
"""
import json, os, re, sys

root, out_path = sys.argv[1], sys.argv[2]


def load(name):
    for candidate in (os.path.join(root, 'config', name), os.path.join(root, name)):
        if os.path.exists(candidate):
            return json.loads(open(candidate, encoding='utf-8-sig').read(), strict=False)
    raise SystemExit(f'missing {name}')


def fn_source(name):
    for sub in ('public', 'private'):
        for candidate in (os.path.join(root, 'functions', sub, name + '.ps1'), os.path.join(root, 'fn', name + '.ps1')):
            if os.path.exists(candidate):
                return open(candidate, encoding='utf-8-sig').read()
    raise SystemExit(f'missing function {name}')


tweaks = load('tweaks.json')
features = load('feature.json')
dns = load('dns.json')

# Downloads from the internet: not taken while osXos promises no network requests.
NETWORK = {'WPFTweaksRemoveEdge', 'WPFTweaksRemoveOneDrive', 'WPFTweaksWindowsAI', 'WPFTweaksBlockAdobeNet',
           'WPFPanelAutologin', 'WPFFixesWinget', 'WPFWinUtilInstallPSProfile', 'WPFWinUtilUninstallPSProfile',
           'WPFOOSUbutton'}
# Already an osXos tool of its own.
DUPLICATE = {'WPFToggleDarkMode', 'WPFToggleShowExt', 'WPFToggleHiddenFiles', 'WPFTweaksRightClickMenu'}

# key -> (slug, category, icon)
SCRIPT_TWEAKS = {
    'WPFTweaksTelemetry': ('telemetry', 'Privacy', 'IconPrivacy'),
    'WPFTweaksLocation': ('location-tracking', 'Privacy', 'IconPrivacy'),
    'WPFTweaksDisableStoreSearch': ('store-search', 'Privacy', 'IconStore'),
    'WPFTweaksServices': ('services-manual', 'System', 'IconServices'),
    'WPFTweaksHiber': ('hibernation', 'System', 'IconSystem'),
    'WPFTweaksDisplay': ('visual-effects', 'System', 'IconImage'),
    'WPFTweaksDisableBitLocker': ('bitlocker', 'System', 'IconPrivacy'),
    'WPFTweaksReservedStorage': ('reserved-storage', 'Maintenance', 'IconMaintenance'),
    'WPFTweaksRestorePoint': ('restore-point', 'System', 'IconRestore'),
    'WPFTweaksDiskCleanup': ('disk-cleanup', 'Maintenance', 'IconTrash'),
    'WPFTweaksDeleteTempFiles': ('temp-files-all', 'Maintenance', 'IconTrash'),
    'WPFTweaksWidget': ('widgets-remove', 'Shell', 'IconViewGrid'),
    'WPFTweaksDisableExplorerAutoDiscovery': ('explorer-folder-discovery', 'Shell', 'IconFolder'),
    'WPFToggleStartMenuRecommendations': ('start-recommendations', 'Shell', 'IconViewGrid'),
    'WPFToggleTaskbarAlignment': ('taskbar-centered', 'Shell', 'IconViewList'),
    'WPFTweaksRazerBlock': ('razer-autoinstall', 'System', 'IconDownload'),
    'WPFTweaksLogiBlock': ('logitech-autoinstall', 'System', 'IconDownload'),
    'WPFTweaksTeredo': ('teredo', 'Network', 'IconNetwork'),
    'WPFTweaksDisableIPv6': ('ipv6', 'Network', 'IconNetwork'),
}
# Entries with registry values that are still one-off jobs rather than settings.
ALWAYS_ACTION = {'WPFTweaksRestorePoint'}
# One-shot entries that cannot be undone.
DESTRUCTIVE = {'WPFTweaksWidget', 'WPFTweaksDiskCleanup', 'WPFTweaksDeleteTempFiles'}

FEATURE_ICONS = {'Features': 'IconPackages', 'Fixes': 'IconMaintenance', 'Legacy Windows Panels': 'IconExternalLink',
                 'Remote Access': 'IconNetwork'}

HIVE = {'HKCU': 'RegHive.CurrentUser', 'HKLM': 'RegHive.LocalMachine', 'HKU': 'RegHive.Users'}
KIND = {'DWord': 'RegValueKind.DWord', 'QWord': 'RegValueKind.QWord', 'String': 'RegValueKind.String'}


def cs(s):
    return '"' + s.replace('\\', '\\\\').replace('"', '\\"').replace('\n', '\\n').replace('\r', '') + '"'


def vs(s):
    """A C# verbatim string, for scripts."""
    return '@"' + s.replace('\r', '').replace('"', '""') + '"'


def clean(d):
    return re.sub(r'\s+', ' ', (d or '').replace('!!THIS TWEAK WILL NOT WORK!!', 'this tweak will not work')).strip()


def name_for(content):
    m = re.match(r'^(.*?) - (Disable|Enable|Remove|Create|Run|Reset|Reinstall|Install|Set to Manual)$', content)
    if m:
        verb = m.group(2)
        return f"{verb} {m.group(1)}" if verb != 'Set to Manual' else f"Set {m.group(1)} to Manual"
    m = re.match(r'^(.*?) - Set to (.*)$', content)
    if m:
        return f"Set {m.group(1)} to {m.group(2)}"
    m = re.match(r'^(.*?) - (Set .*)$', content)
    if m:
        return m.group(2)
    return content.strip()


def registry(v):
    out = []
    for r in v.get('registry') or []:
        if 'Value' not in r:
            continue
        hive, path = r['Path'].split(':\\', 1)
        if hive == 'HKU' and path.lower().startswith('.default'):
            path = '.DEFAULT' + path[len('.default'):]
        orig = r.get('OriginalValue')
        orig_cs = 'null' if orig == '<RemoveEntry>' else cs(str(orig))
        out.append(f"new({HIVE[hive]}, {cs(path)}, {cs(r['Name'])}, {KIND[r['Type']]}, {cs(str(r['Value']))}, {orig_cs}, {cs(r['Name'])})")
    return out


def services(v):
    return [f"new({cs(s['Name'])}, {cs(s['StartupType'])}, {cs(s['OriginalType'])})" for s in v.get('service') or []]


def joined(v, key):
    parts = [s.strip() for s in v.get(key) or [] if s and s.strip()]
    return '\n\n'.join(parts) if parts else None


entries = []


def emit(slug, category, name, summary, icon, kind, steps, reg=(), svc=(), feats=(), script=None, undo=None,
         destructive=False, caution=None, restart=False, applied=None, not_applied=None, preference=False):
    body = [f"        new({cs(slug)}, ToolCategory.{category}, {cs(name)}, {cs(summary)}, {cs(icon)}, ScriptKind.{kind},",
            "            new ToolStep[]", "            {"]
    for t, d in steps:
        body.append(f"                new({cs(t)}, {cs(d)}),")
    body.append("            })")
    body.append("        {")
    if reg:
        body.append("            Registry = new TweakValue[] { " + ", ".join(reg) + " },")
    if svc:
        body.append("            Services = new ServiceChange[] { " + ", ".join(svc) + " },")
    if feats:
        body.append("            Features = new[] { " + ", ".join(cs(f) for f in feats) + " },")
    if script:
        body.append(f"            Script = {vs(script)},")
    if undo:
        body.append(f"            UndoScript = {vs(undo)},")
    if destructive:
        body.append("            Destructive = true,")
    if caution:
        body.append(f"            Caution = {cs(caution)},")
    if restart:
        body.append("            NeedsRestart = true,")
    if preference:
        body.append("            IsPreference = true,")
    if applied:
        body.append(f"            AppliedLabel = {cs(applied)},")
    if not_applied:
        body.append(f"            NotAppliedLabel = {cs(not_applied)},")
    body.append("        },")
    entries.append('\n'.join(body))


CAUTION = "WinUtil lists this among its advanced tweaks: apply it only if you know you want it."
CREDIT = "Taken from Chris Titus Tech's WinUtil (MIT licensed): its values, service changes and PowerShell, word for word."


def standard_steps(desc, what, how_undo, elevated=True):
    return [
        ("What it does", desc),
        ("What runs", what),
        ("How it runs", ("One elevated PowerShell — Windows asks for administrator rights once — with WinUtil's script unmodified. "
                         "The Review stage shows every value, service and line of script before anything happens, and the Result stage shows what the script printed.")
         if elevated else "Opens the panel. Nothing on the system changes and no administrator rights are needed."),
        ("Undo, and where this comes from", how_undo + " " + CREDIT),
    ]


# ---------------------------------------------------------------- script tweaks
for key, (slug, cat, icon) in SCRIPT_TWEAKS.items():
    v = tweaks[key]
    assert key not in NETWORK and key not in DUPLICATE
    reg, svc = registry(v), services(v)
    script, undo = joined(v, 'InvokeScript'), joined(v, 'UndoScript')
    pref = v.get('Type') == 'Toggle'
    # A restore point sets one registry value as a means to an end; it is a job, not a setting.
    kind = 'Toggle' if (reg or svc) and key not in ALWAYS_ACTION else 'Action'
    name = name_for(v['Content'])
    desc = clean(v.get('Description')) or name
    what_parts = []
    if reg: what_parts.append(f"{len(reg)} registry value{'s' if len(reg) != 1 else ''}")
    if svc: what_parts.append(f"{len(svc)} service start type{'s' if len(svc) != 1 else ''}")
    if script: what_parts.append("WinUtil's PowerShell script")
    what = "It sets " + ", ".join(what_parts) + " — each listed on the Review stage." if what_parts else "Nothing beyond the script."
    how_undo = ("Running it again restores the original values and start types" + (" and runs WinUtil's undo script." if undo else ".")
                if kind == 'Toggle' else
                ("There is a separate Undo tool beside it, running WinUtil's undo script." if undo else "WinUtil offers no undo for this."))
    verb = name.split(' ', 1)[0]
    labels = {'Disable': ('Disabled', 'Enabled'), 'Enable': ('Enabled', 'Not enabled'), 'Set': ('Set', 'Not set')}.get(verb, (None, None))
    if pref:
        labels = ('On', 'Off')
    emit('winutil-' + slug, cat, name, desc, icon, kind, standard_steps(desc, what, how_undo),
         reg=reg, svc=svc, script=script, undo=undo if kind == 'Toggle' else None,
         destructive=key in DESTRUCTIVE, caution=CAUTION if v.get('category', '').startswith('z__') else None,
         applied=labels[0], not_applied=labels[1], preference=pref)
    if kind == 'Action' and undo:
        emit('winutil-' + slug + '-undo', cat, 'Undo: ' + name, 'Reverse ' + name + ' with WinUtil\'s own undo script.', icon, 'Action',
             standard_steps("Reverses " + name + ".", "WinUtil's undo script, shown in full on the Review stage.", "Run the tool beside it to apply it again."),
             script=undo, caution=CAUTION if v.get('category', '').startswith('z__') else None)

# ---------------------------------------------------------------- features, panels
for key, v in features.items():
    if key in NETWORK or v.get('Type') == 'Button' and v.get('function') and key not in (
            'WPFFixesNTPPool', 'WPFFixesUpdate', 'WPFFixesNetwork', 'WPFPanelDISM', 'WPFWinUtilSSHServer'):
        continue
    cat_src = v.get('category', '')
    desc = clean(v.get('Description')) or v['Content']
    name = name_for(v['Content'])
    slug = 'winutil-' + re.sub(r'[^a-z0-9]+', '-', key.lower().replace('wpffeatures', '').replace('wpffeature', '').replace('wpffixes', 'fix-').replace('wpfpanel', 'panel-').replace('wpfwinutil', '')).strip('-')

    if cat_src == 'Legacy Windows Panels':
        emit(slug, 'System', 'Open ' + v['Content'], f"Open the classic {v['Content']} window.", 'IconExternalLink', 'Launcher',
             standard_steps(desc, "One command that opens the panel, shown on the Review stage.", "Nothing to undo — it only opens a window.", elevated=False),
             script=joined(v, 'InvokeScript'))
        continue

    if v.get('function'):
        fn = v['function']
        src = fn_source(fn)
        call = fn
        if fn == 'Invoke-WPFSSHServer':
            src = fn_source('Invoke-WinUtilSSHServer') + '\n\n' + src
        if fn == 'Invoke-WPFSystemRepair':
            desc += ' Runs chkdsk /scan, sfc /scannow and DISM /RestoreHealth in turn; this takes a while.'
        category = 'Network' if fn in ('Invoke-WPFFixesNetwork', 'Invoke-WPFFixesNTPPool', 'Invoke-WPFSSHServer') else 'Maintenance'
        emit(slug, category, name, desc, 'IconMaintenance' if category == 'Maintenance' else 'IconNetwork', 'Action',
             standard_steps(desc, f"WinUtil's {fn} function, embedded in full and shown on the Review stage.", "WinUtil offers no undo for this."),
             script=src.strip() + '\n\n' + call, restart=fn in ('Invoke-WPFFixesNetwork', 'Invoke-WPFSystemRepair'))
        continue

    feats = v.get('feature') or []
    script = joined(v, 'InvokeScript')
    kind = 'Feature' if feats else 'Action'
    what = (f"Turns on the Windows optional feature{'s' if len(feats) != 1 else ''} {', '.join(feats)}" + (" and runs WinUtil's script." if script else ".")) if feats else "WinUtil's script."
    emit(slug, 'System', name, desc, 'IconPackages', kind,
         standard_steps(desc, what, "Features are turned off again from Settings → System → Optional features, or Turn Windows features on or off." if feats else "WinUtil offers no undo for this."),
         feats=feats, script=script, restart=bool(feats))

# ---------------------------------------------------------------- DNS
dns_json = json.dumps(dns)
dns_src = fn_source('Set-WinUtilDNS')
for provider in list(dns.keys()) + ['DHCP']:
    pretty = provider.replace('_', ' ')
    if provider == 'DHCP':
        name, summary = 'Reset DNS to Automatic (DHCP)', 'Give every active network adapter back the DNS servers your router hands out.'
    else:
        p = dns[provider]
        name = f"Set DNS to {pretty}"
        summary = f"Point every active network adapter at {pretty}: {p.get('Primary')}, {p.get('Secondary')}."
    script = "$sync = @{ configs = @{ dns = (ConvertFrom-Json @'\n" + dns_json + "\n'@) } }\n\n" + dns_src.strip() + f"\n\nSet-WinUtilDNS -DNSProvider '{provider}'"
    emit('winutil-dns-' + provider.lower().replace('_', '-'), 'Network', name, summary, 'IconNetwork', 'Action',
         standard_steps(summary, "WinUtil's Set-WinUtilDNS, embedded in full with its provider list, run against every adapter that is up.",
                        "Reset DNS to Automatic (DHCP) puts the router's servers back."),
         script=script)

# ---------------------------------------------------------------- power plans
power = fn_source('Invoke-WPFUltimatePerformance').strip()
emit('winutil-ultimate-performance', 'System', 'Enable Ultimate Performance Power Plan',
     'Add the hidden Ultimate Performance power plan and switch to it.', 'IconSystem', 'Action',
     standard_steps('Adds the hidden Ultimate Performance power plan and switches to it.', "WinUtil's Invoke-WPFUltimatePerformance -Enable.",
                    'Restore Default Power Plans puts the standard plans back.'),
     script=power + '\n\nInvoke-WPFUltimatePerformance -Enable')
emit('winutil-default-power-plans', 'System', 'Restore Default Power Plans',
     'Reset Windows power plans to their defaults, removing Ultimate Performance and any custom plans.', 'IconSystem', 'Action',
     standard_steps('Resets Windows power plans to their defaults — custom plans, including Ultimate Performance, are removed.',
                    "WinUtil's Invoke-WPFUltimatePerformance without -Enable (powercfg /restoredefaultschemes).", 'Enable Ultimate Performance Power Plan adds it back.'),
     script=power + '\n\nInvoke-WPFUltimatePerformance', destructive=True)

# ---------------------------------------------------------------- Windows Update policies
UPDATES = [
    ('default', 'Invoke-WPFUpdatesdefault', 'Windows Update: Default Settings', 'Put Windows Update back to Microsoft\'s defaults, undoing any deferral or disable.'),
    ('security', 'Invoke-WPFUpdatessecurity', 'Windows Update: Security Only (Recommended)', 'Defer feature updates a year and quality updates four days, stop driver offers, and ask before installing.'),
    ('disable', 'Invoke-WPFUpdatesdisable', 'Windows Update: Disable', 'Turn Windows Update off entirely. WinUtil warns this is not recommended: no security fixes arrive.'),
]
for slug, fn, name, summary in UPDATES:
    emit('winutil-updates-' + slug, 'System', name, summary, 'IconDownload', 'Action',
         standard_steps(summary, f"WinUtil's {fn}, embedded in full and shown on the Review stage.",
                        "Windows Update: Default Settings undoes either of the others."),
         script=fn_source(fn).strip() + '\n\n' + fn,
         caution="Not recommended: with updates off, security fixes stop arriving." if slug == 'disable' else None,
         destructive=slug == 'disable')

header = '''// <auto-generated>
// Generated by scripts/gen-winutil-scripts.py from Chris Titus Tech's WinUtil
// (https://github.com/ChrisTitusTech/winutil, MIT License, Copyright (c) 2022 CT Tech Group LLC):
// config/tweaks.json, config/feature.json, config/dns.json and the functions it calls.
// WinUtil's scripts are embedded unmodified. Regenerate rather than edit.
// </auto-generated>

namespace OsXos.Tools.Windows;

/// <summary>The WinUtil entries that need a script, services, features or a panel.</summary>
public static class WinUtilScripts
{
    public static IReadOnlyList<WinUtilScript> All { get; } = new WinUtilScript[]
    {
'''
footer = '''    };
}
'''
open(out_path, 'w', encoding='utf-8', newline='\n').write(header + '\n'.join(entries) + '\n' + footer)
print('generated', len(entries))
