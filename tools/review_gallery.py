import argparse
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageOps


def make(folder, mode, dpi):
    report = json.loads((folder/'review.json').read_text(encoding='utf-8'))
    images = [x['Surface'] for x in report['Captures'] if x['Theme'] == mode and abs(x['Scaling'] - dpi/96)<.001]
    width, height = 430, 287
    bg, fg = ('#202227','#e9edf3') if mode=='dark' else ('#e9ebef','#252932')
    sheet=Image.new('RGB',(width*3,height*((len(images)+2)//3)),bg); pen=ImageDraw.Draw(sheet)
    font=ImageFont.truetype('segoeui.ttf',17)
    for index,key in enumerate(images):
        with Image.open(folder/f'{key}-{mode}-{dpi}.png') as src:
            img=src.convert('RGBA')
            if key=='classroom' and (box:=img.getchannel('A').getbbox()): img=img.crop(box)
            img.thumbnail((width-18,height-42),Image.Resampling.LANCZOS)
            x=index%3*width; y=index//3*height
            sheet.paste(img,(x+(width-img.width)//2,y+28+(height-35-img.height)//2),img)
            pen.text((x+10,y+4),key,font=font,fill=fg)
    sheet.save(folder/f'gallery-{mode}-{dpi}.jpg',quality=89,optimize=True)


if __name__=='__main__':
    p=argparse.ArgumentParser(); p.add_argument('directory',type=Path); args=p.parse_args()
    for variant in ('dark','light'):
        for resolution in (96,120,144): make(args.directory,variant,resolution)
