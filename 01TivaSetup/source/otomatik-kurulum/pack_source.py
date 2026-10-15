"""Create a shareable source bundle from an explicit public-file list."""
from pathlib import Path
import zipfile

ROOT=Path(__file__).resolve().parent.parent
SRC=ROOT/'otomatik-kurulum'
OUT=ROOT/'output/windows'
OUT.mkdir(parents=True,exist_ok=True)
files=['Program.cs','MainForm.cs','Localization.cs','Theme.cs','ThemedText.cs','DeviceFamilies.cs','build.py','pack_source.py',
       'make_icon.py','app.manifest','flash.js','translations.json','help.en.txt','help.tr.txt',
       'licenses.en.txt','licenses.tr.txt','KULLANIM.txt','.gitignore',
       'tiva-setup.svg','tiva-setup.png','tiva-setup.ico']
readme=(SRC/'README.md').read_text(encoding='utf-8')
preview='\n![Tiva Setup](assets/TivaSetup-EN.jpg)\n\n![Dark theme](assets/TivaSetup-Dark.jpg)\n'
target=OUT/'TivaSetup-source.zip'
with zipfile.ZipFile(target,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=9) as z:
    z.writestr('README.md',readme.replace('# Tiva Setup\n','# Tiva Setup\n'+preview,1))
    z.writestr('.gitignore','/indirilenler/\n/output/\n/otomatik-kurulum/build/\n*.user\n*.suo\n')
    for name in files:z.write(SRC/name,'otomatik-kurulum/'+name)
    z.write(ROOT/'kurulum-kaydi/ilk_program_main.c','kurulum-kaydi/ilk_program_main.c')
    for name in ['TivaSetup-EN.jpg','TivaSetup-Dark.jpg','TivaSetup-Licenses-Dark.jpg','TivaSetup-Families-Dark.jpg','TivaSetup-Families-Light.jpg']:
        if (OUT/name).exists():z.write(OUT/name,'assets/'+name)
    if (OUT/'TivaSetup-TEST_RESULTS.txt').exists():z.write(OUT/'TivaSetup-TEST_RESULTS.txt','TEST_RESULTS.txt')
with zipfile.ZipFile(target) as z:
    assert z.testzip() is None
    print('Source ZIP verified:',len(z.namelist()),'files,',target.stat().st_size,'bytes')
