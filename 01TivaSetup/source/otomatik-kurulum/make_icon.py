"""Render the project's original vector-style chip mark into Windows icon sizes."""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT=Path(__file__).resolve().parent
SCALE=4
def icon(size):
    im=Image.new('RGBA',(256*SCALE,256*SCALE),(0,0,0,0))
    d=ImageDraw.Draw(im)
    def box(b):return tuple(int(x*SCALE) for x in b)
    def line(points,fill,width):d.line([(int(x*SCALE),int(y*SCALE)) for x,y in points],fill=fill,width=int(width*SCALE))
    navy='#111827';mint='#51d4bd';white='#e6edf5'
    d.rounded_rectangle(box((4,4,252,252)),radius=54*SCALE,fill=navy)
    for n in (88,128,168):
        line([(n,47),(n,66)],mint,8);line([(n,190),(n,209)],mint,8)
        line([(47,n),(66,n)],mint,8);line([(190,n),(209,n)],mint,8)
    d.rounded_rectangle(box((66,66,190,190)),radius=20*SCALE,outline=mint,width=8*SCALE)
    # A connected route evokes GPIO and the setup path; RGB dots reference the demo.
    line([(96,155),(96,110),(153,110),(153,91)],white,10)
    for x,y in [(96,155),(153,91)]:d.ellipse(box((x-8,y-8,x+8,y+8)),fill=white)
    for x,color in [(126,'#ff7987'),(148,mint),(170,'#71a9ff')]:
        d.ellipse(box((x-6,151,x+6,163)),fill=color)
    return im.resize((size,size),Image.Resampling.LANCZOS)

icon(256).save(ROOT/'tiva-setup.png')
icon(256).save(ROOT/'tiva-setup.ico',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
