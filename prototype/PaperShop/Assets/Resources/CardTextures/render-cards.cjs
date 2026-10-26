// Re-render the editable SVG sources and validate the transparent character cards.
// Usage: node render-cards.cjs [absolute path to sharp's package directory]
const fs = require('node:fs/promises');
const path = require('node:path');
const sharp = require(process.argv[2] || 'sharp');

async function inspect(file) {
  const {data, info} = await sharp(file).ensureAlpha().raw().toBuffer({resolveWithObject:true});
  let minX=info.width, minY=info.height, maxX=-1, maxY=-1, transparent=0, partial=0, opaque=0;
  for (let y=0;y<info.height;y++) for(let x=0;x<info.width;x++) {
    const a=data[(y*info.width+x)*4+3];
    if (a===0) transparent++; else {
      if(a===255) opaque++; else partial++;
      minX=Math.min(minX,x); minY=Math.min(minY,y); maxX=Math.max(maxX,x); maxY=Math.max(maxY,y);
    }
  }
  if(info.width!==512 || info.height!==768 || info.channels!==4) throw Error('Bad size or channels: '+file);
  if(!transparent || !opaque || minX<12 || minY<12 || maxX>=500 || maxY>=756) throw Error('Invalid alpha or clipped silhouette: '+file);
  return {name:path.basename(file),width:info.width,height:info.height,channels:info.channels,alphaBounds:{minX,minY,maxX,maxY},transparentPixels:transparent,partialAlphaPixels:partial,opaquePixels:opaque,pivot:{x:256,y:736}};
}
(async()=>{
  const reports=[];
  for(const pose of ['idle','work']) {
    const source=path.join(__dirname,pose+'.svg'), target=path.join(__dirname,pose+'.png');
    await fs.access(source);
    await sharp(source,{density:72}).png({compressionLevel:9}).toFile(target);
    reports.push(await inspect(target));
  }
  if(reports[0].alphaBounds.maxY!==reports[1].alphaBounds.maxY) throw Error('Feet do not share a baseline');
  if(reports[0].alphaBounds.maxY!==735) throw Error('Expected geometric foot baseline y=736, last occupied pixel y=735');
  console.log(JSON.stringify({status:'passed',reports},null,2));
})().catch(error=>{console.error(error);process.exitCode=1;});

