"""Build a Windows 10/11 .NET Framework WinForms launcher and an offline EXE.
Only public installer files and example code are packaged. No user/profile files.
"""
from pathlib import Path
import hashlib, json, re, shutil, struct, subprocess, zipfile, sys

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / 'otomatik-kurulum'
OUT = ROOT / 'output/windows'
OUT.mkdir(parents=True, exist_ok=True)
BUILD = SRC / 'build'
BUILD.mkdir(exist_ok=True)
TI = Path('C:/ti')

packages = [
    dict(Id='ccs', Name='Code Composer Studio 12.8.1', File='CCS12.8.1.00005_win64.zip',
         Hash='db6318483fb6f0358c4d65a42610581844f9b6f7c1048f42be28d8804f378e8c',
         Url='https://dr-download.ti.com/software-development/ide-configuration-compiler-or-debugger/MD-J1VdearkvK/12.8.1/CCS12.8.1.00005_win64.zip',
         Page='https://www.ti.com/tool/download/CCSTUDIO/12.8.1'),
    dict(Id='icdi', Name='Stellaris ICDI 2.0.12245', File='stellaris_icdi_drivers_12245.zip',
         Hash='1fa07153e2c05e9cb6a0a2967806dad8a22257b96517eef9f866a829f6f5e30d',
         Url='https://dr-download.ti.com/software-development/code-example-or-demo/MD-Sjw8b9G4TT/2.0.1.11577/stellaris_icdi_drivers_12245.zip',
         Page='https://www.ti.com/tool/SW-EK-TM4C123GXL'),
    dict(Id='tiva', Name='TivaWare 2.2.0.295', File='SW-TM4C-2.2.0.295.exe',
         Hash='87e34c6b199ce441c0ac6c9c873698ca2d10e66e62dd1fd478377f6da9e56a9b', Url='',
         Page='https://www.ti.com/tool/download/SW-TM4C/2.2.0.295'),
    dict(Id='lm', Name='LM Flash Programmer 1.0.1588', File='LMFlashProgrammer_1588.zip',
         Hash='7252de1951903ff4c43f320518acc700916df52a36e3837f530df2becd8657a1',
         Url='https://dr-download.ti.com/software-development/code-example-or-demo/MD-Sjw8b9G4TT/2.0.1.11577/LMFlashProgrammer_1588.zip',
         Page='https://www.ti.com/tool/SW-EK-TM4C123GXL'),
]
for p in packages:
    file = ROOT / 'indirilenler' / p['File']
    with file.open('rb') as f:
        assert hashlib.file_digest(f, 'sha256').hexdigest() == p['Hash'], p['File']
(BUILD/'packages.json').write_text(json.dumps(packages,ensure_ascii=False),encoding='utf-8')
sdk_hashes = {}
tiva_licenses = {}
# Read verified distribution contents; building does not require an installed SDK.
with zipfile.ZipFile(ROOT/'indirilenler'/'SW-TM4C-2.2.0.295.exe') as sdk_archive:
    for name in ['inc/hw_memmap.h','driverlib/gpio.h','driverlib/sysctl.h','driverlib/ccs/Debug/driverlib.lib']:
        with sdk_archive.open(name) as f:
            sdk_hashes[name] = hashlib.file_digest(f,'sha256').hexdigest()
    for name in ['EULA.txt','TI-BSD-EULA.txt']:
        tiva_licenses[name] = sdk_archive.read(name).decode('cp1252',errors='replace')
(BUILD/'sdk-hashes.json').write_text(json.dumps(sdk_hashes),encoding='utf-8')
shutil.copy2(ROOT/'kurulum-kaydi/ilk_program_main.c',BUILD/'main.c')
shutil.copy2(SRC/'KULLANIM.txt',OUT/'KULLANIM.txt')
for name in ['help.en.txt','help.tr.txt','licenses.en.txt','licenses.tr.txt','translations.json']:
    shutil.copy2(SRC/name,BUILD/name)
# Preserve all TI licensing text; the vendor archives are also unchanged.
from html.parser import HTMLParser
class LicenseText(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.parts = []
    def handle_data(self, data):
        if data.strip(): self.parts.append(data.strip())
parser = LicenseText()
manifest=BUILD/'ccs12-manifest.html'
if not manifest.exists():
    manifests=list((TI/'ccs1281/ccs/doc').glob('*manifest*.html'))
    if not manifests: raise RuntimeError('CCS 12 lisans manifesti gerekli; CCS 8 lisans metni kullanılmaz.')
    shutil.copy2(manifests[0],manifest)
parser.feed(manifest.read_text(encoding='utf-8',errors='replace'))
ccs_licenses = '\n'.join(parser.parts)
license_text = 'TIVA SETUP — ORIGINAL VENDOR LICENSES\n\nIndependent tool; not a Texas Instruments product.\nOriginal vendor installer files and license terms are preserved.\n\nCCS LICENSE / MANIFEST\n\n' + ccs_licenses
for name in ['EULA.txt','TI-BSD-EULA.txt']:
    license_text += '\n\nTIVAWARE '+name+'\n\n'+tiva_licenses[name]
(BUILD/'licenses.txt').write_text(license_text,encoding='utf-8')
csc=Path('C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe')
exe=OUT/'TivaSetupLite-3.4.exe'
args=[str(csc),'/nologo','/target:winexe','/platform:anycpu','/optimize+','/codepage:65001',
      '/out:'+str(exe),'/win32icon:'+str(SRC/'tiva-setup.ico'),'/win32manifest:'+str(SRC/'app.manifest')]
for lib in ['System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Management.dll',
            'System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.Web.Extensions.dll']:
    args += ['/reference:'+lib]
for name in ['main.c','packages.json','sdk-hashes.json','licenses.txt','help.en.txt','help.tr.txt','licenses.en.txt','licenses.tr.txt','translations.json']:
    args += ['/resource:'+str(BUILD/name)+','+name]
for name in ['tiva-setup.ico','tiva-setup.png']:
    args += ['/resource:'+str(SRC/name)+','+name]
args += ['/resource:'+str(SRC/'flash.js')+',flash.js',str(SRC/'Program.cs'),str(SRC/'MainForm.cs'),str(SRC/'Localization.cs'),str(SRC/'Theme.cs'),str(SRC/'ThemedText.cs'),str(SRC/'DeviceFamilies.cs')]
subprocess.run(args,check=True)
if '--launcher-only' in sys.argv:
    print('Launcher built:', exe)
    sys.exit(0)
payload=BUILD/'payload.zip'
with zipfile.ZipFile(payload,'w',compression=zipfile.ZIP_STORED,allowZip64=True) as z:
    for p in packages:
        z.write(ROOT/'indirilenler'/p['File'],p['File'])
full=OUT/'TivaSetup-3.4.exe'
with full.open('wb') as f:
    with exe.open('rb') as src: shutil.copyfileobj(src,f,4*1024*1024)
    with payload.open('rb') as src: shutil.copyfileobj(src,f,4*1024*1024)
    f.write(b'TM4C_PAYLOAD_V1!'+struct.pack('<q',payload.stat().st_size))
payload.unlink()
with full.open('rb') as f:
    full_sha=hashlib.file_digest(f,'sha256').hexdigest()
(OUT/'TivaSetup-SHA256.txt').write_text(full_sha+'  '+full.name+'\n',encoding='ascii')
print(json.dumps({'file':str(full),'bytes':full.stat().st_size,'sha256':full_sha},indent=2))
