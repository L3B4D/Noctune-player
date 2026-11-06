'use strict';
const Kuroshiro = require('kuroshiro').default;
const Analyzer = require('kuroshiro-analyzer-kuromoji');
const Korean=require('aromanize');
let input='';
process.stdin.setEncoding('utf8');
process.stdin.on('data',chunk=>{input+=chunk;if(input.length>500000)process.exit(2)});
process.stdin.on('end',async()=>{
  try {
    const lines=JSON.parse(input);
    if(!Array.isArray(lines)||lines.length>4000||lines.some(x=>typeof x!=='string'||x.length>6000))throw Error('Invalid lines');
    const engine=new Kuroshiro();await engine.init(new Analyzer());
    const results=[];
    for(const line of lines){let result=/[\u3040-\u30ff\u3400-\u9fff]/.test(line)?await engine.convert(line,{to:'romaji',mode:'spaced',romajiSystem:'hepburn'}):line;results.push(/[\uac00-\ud7af]/.test(result)?Korean.hangulToLatin(result,'rr'):result!==line?result:'');}
    process.stdout.write(JSON.stringify(results));
  } catch(e) {process.stderr.write('Pronunciation could not be generated.');process.exitCode=1;}
});
