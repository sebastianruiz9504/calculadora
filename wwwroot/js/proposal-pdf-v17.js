// Digital Tech - motor PDF adaptado a la presentación visual V20.
// Conserva intacto el contrato económico mensual/contrato alimentado por la calculadora.
// Fuente visual aprobada: plantilla_interactiva_cotizaciones_digital_tech_v20.html.
function DTPDF(images){
  this.pages=[];              // {ops:[], imgs:Set}
  this.cur=null;
  this.W=595.28; this.H=841.89;
  this.mL=56; this.mR=56; this.mT=116; this.mB=88;
  this.images=images||{};     // { name: {data(bin str), w, h} }
  this._newPage();
}
DTPDF.prototype._newPage=function(){ this.cur={ops:[], imgs:{}}; this.pages.push(this.cur); this.y=this.H-this.mT; };
DTPDF.prototype.addPage=function(){ this._newPage(); };
DTPDF.prototype._enc=function(s){
  s=String(s==null?'':s);
  var map={'\u2022':0x95,'\u2013':0x96,'\u2014':0x97,'\u2018':0x91,'\u2019':0x92,'\u201C':0x93,'\u201D':0x94,'\u20AC':0x80,'\u2026':0x85,'\u00A0':0x20};
  var out='';
  for(var i=0;i<s.length;i++){
    var c=s.charCodeAt(i);
    if(map[s[i]]!=null) c=map[s[i]];
    if(c>255) c=0x3F;
    if(c===0x28||c===0x29||c===0x5C){ out+='\\'+String.fromCharCode(c); } else out+=String.fromCharCode(c);
  }
  return out;
};
DTPDF.prototype.rect=function(x,y,w,h,rgb){
  var c=rgb||[0,0,0];
  this.cur.ops.push((c[0]/255).toFixed(3)+' '+(c[1]/255).toFixed(3)+' '+(c[2]/255).toFixed(3)+' rg');
  this.cur.ops.push(x.toFixed(2)+' '+y.toFixed(2)+' '+w.toFixed(2)+' '+h.toFixed(2)+' re f');
};
DTPDF.prototype.rectStroke=function(x,y,w,h,rgb,wd){
  var c=rgb||[200,210,220];
  this.cur.ops.push((wd||1).toFixed(2)+' w '+(c[0]/255).toFixed(3)+' '+(c[1]/255).toFixed(3)+' '+(c[2]/255).toFixed(3)+' RG');
  this.cur.ops.push(x.toFixed(2)+' '+y.toFixed(2)+' '+w.toFixed(2)+' '+h.toFixed(2)+' re S');
};
DTPDF.prototype.line=function(x1,y1,x2,y2,rgb,wd){
  var c=rgb||[210,220,230];
  this.cur.ops.push((wd||0.7).toFixed(2)+' w '+(c[0]/255).toFixed(3)+' '+(c[1]/255).toFixed(3)+' '+(c[2]/255).toFixed(3)+' RG');
  this.cur.ops.push(x1.toFixed(2)+' '+y1.toFixed(2)+' m '+x2.toFixed(2)+' '+y2.toFixed(2)+' l S');
};
DTPDF.prototype.image=function(name,x,y,w,h){
  if(!this.images[name]) return;
  this.cur.imgs[name]=true;
  this.cur.ops.push('q '+w.toFixed(2)+' 0 0 '+h.toFixed(2)+' '+x.toFixed(2)+' '+y.toFixed(2)+' cm /'+name+' Do Q');
};
DTPDF.prototype.imageFullWidthTop=function(name){
  var im=this.images[name]; if(!im) return;
  var h=this.W*im.h/im.w;
  this.image(name,0,this.H-h,this.W,h);
};
DTPDF.prototype.imageFullWidthBottom=function(name){
  var im=this.images[name]; if(!im) return;
  var h=this.W*im.h/im.w;
  this.image(name,0,0,this.W,h);
};
DTPDF.prototype._textAbs=function(x,y,txt,size,bold,rgb,ls){
  var c=rgb||[20,32,51]; var f=bold?'F2':'F1';
  this.cur.ops.push('BT');
  this.cur.ops.push((c[0]/255).toFixed(3)+' '+(c[1]/255).toFixed(3)+' '+(c[2]/255).toFixed(3)+' rg');
  if(ls) this.cur.ops.push(ls.toFixed(2)+' Tc');
  this.cur.ops.push('/'+f+' '+size+' Tf 1 0 0 1 '+x.toFixed(2)+' '+y.toFixed(2)+' Tm ('+this._enc(txt)+') Tj ET');
  if(ls) this.cur.ops.push('0 Tc');
};
var HW={' ':278,'!':278,'"':355,'#':556,'$':556,'%':889,'&':667,"'":191,'(':333,')':333,'*':389,'+':584,',':278,'-':333,'.':278,'/':278,'0':556,'1':556,'2':556,'3':556,'4':556,'5':556,'6':556,'7':556,'8':556,'9':556,':':278,';':278,'<':584,'=':584,'>':584,'?':556,'@':1015,'A':667,'B':667,'C':722,'D':722,'E':667,'F':611,'G':778,'H':722,'I':278,'J':500,'K':667,'L':556,'M':833,'N':722,'O':778,'P':667,'Q':778,'R':722,'S':667,'T':611,'U':722,'V':667,'W':944,'X':667,'Y':667,'Z':611,'[':278,'\\':278,']':278,'^':469,'_':556,'`':333,'a':556,'b':556,'c':500,'d':556,'e':556,'f':278,'g':556,'h':556,'i':222,'j':222,'k':500,'l':222,'m':833,'n':556,'o':556,'p':556,'q':556,'r':333,'s':500,'t':278,'u':556,'v':500,'w':722,'x':500,'y':500,'z':500,'{':334,'|':260,'}':334,'~':584};
DTPDF.prototype._cw=function(ch){ var a=ch.charCodeAt(0); if(a>=0xC0&&a<=0xFF) return 556; return HW[ch]!=null?HW[ch]:556; };
DTPDF.prototype._tw=function(txt,size,bold,ls){
  txt=String(txt==null?'':txt);
  var w=0; for(var i=0;i<txt.length;i++) w+=this._cw(txt[i]);
  return w/1000*size*(bold?1.03:1)+Math.max(0,txt.length-1)*(ls||0);
};
DTPDF.prototype._splitToken=function(token,size,bold,maxw,ls){
  token=String(token==null?'':token);
  if(!token) return [''];
  var pieces=[], part='';
  for(var i=0;i<token.length;i++){
    var candidate=part+token[i];
    if(part && this._tw(candidate,size,bold,ls)>maxw){ pieces.push(part); part=token[i]; }
    else part=candidate;
  }
  if(part) pieces.push(part);
  return pieces;
};
DTPDF.prototype._wrap=function(txt,size,bold,maxw,ls){
  var paragraphs=String(txt==null?'':txt).replace(/\r\n?/g,'\n').split('\n'), lines=[];
  for(var pidx=0;pidx<paragraphs.length;pidx++){
    var paragraph=paragraphs[pidx].trim();
    if(!paragraph){ lines.push(''); continue; }
    var words=paragraph.split(/\s+/), cur='';
    for(var i=0;i<words.length;i++){
      var pieces=this._tw(words[i],size,bold,ls)>maxw
        ? this._splitToken(words[i],size,bold,maxw,ls)
        : [words[i]];
      for(var j=0;j<pieces.length;j++){
        var t=cur?cur+' '+pieces[j]:pieces[j];
        if(cur && this._tw(t,size,bold,ls)>maxw){ lines.push(cur); cur=pieces[j]; }
        else cur=t;
        // A token fragment must end its line; joining it with a space would alter the token.
        if(j<pieces.length-1 && cur){ lines.push(cur); cur=''; }
      }
    }
    if(cur) lines.push(cur);
  }
  return lines.length?lines:[''];
};
// Wrapped text within an explicit box; returns final y (baseline advanced)
DTPDF.prototype.textBox=function(x,y,w,txt,o){
  o=o||{}; var size=o.size||11, bold=!!o.bold, rgb=o.color||[73,88,107], lh=o.lh||(size*1.42), align=o.align||'left';
  var lines=this._wrap(txt,size,bold,w);
  for(var i=0;i<lines.length;i++){
    var lx=x;
    if(align==='center'){ lx=x+(w-this._tw(lines[i],size,bold))/2; }
    else if(align==='right'){ lx=x+(w-this._tw(lines[i],size,bold)); }
    this._textAbs(lx,y,lines[i],size,bold,rgb,o.ls);
    y-=lh;
  }
  return y;
};
DTPDF.prototype.textHeight=function(txt,size,bold,w,lh){ lh=lh||(size*1.42); return this._wrap(txt,size,bold,w).length*lh; };

// ------- flowing helpers on content pages -------
DTPDF.prototype.para=function(txt,o){
  o=o||{}; var size=o.size||11.5, bold=!!o.bold, rgb=o.color||[73,88,107], lh=o.lh||(size*1.5);
  var x=this.mL, w=this.W-this.mL-this.mR;
  var lines=this._wrap(txt,size,bold,w);
  for(var i=0;i<lines.length;i++){ this._textAbs(x,this.y-size,lines[i],size,bold,rgb); this.y-=lh; }
  if(o.after) this.y-=o.after;
};
DTPDF.prototype.kicker=function(txt){
  var size=10, ls=2.2, lines=this._wrap(String(txt).toUpperCase(),size,true,this.W-this.mL-this.mR,ls);
  for(var i=0;i<lines.length;i++){ this._textAbs(this.mL,this.y-10,lines[i],size,true,[22,152,200],ls); this.y-=14; }
  this.y-=8;
};
DTPDF.prototype.h1=function(a,cy){
  var x=this.mL; this._textAbs(x,this.y-24,a,25,true,[10,42,82]);
  if(cy){ var wa=this._tw(a,25,true)*1.05+this._tw(' ',25,true); this._textAbs(x+wa,this.y-24,cy,25,true,[22,184,216]); }
  this.y-=40;
};
DTPDF.prototype.h2=function(a,cy){
  var x=this.mL; this._textAbs(x,this.y-18,a,18,true,[10,42,82]);
  if(cy){ var wa=this._tw(a,18,true)*1.05+this._tw(' ',18,true); this._textAbs(x+wa,this.y-18,cy,18,true,[22,184,216]); }
  this.y-=26;
};
DTPDF.prototype.bullet=function(txt,o){
  o=o||{}; var size=o.size||11.5, rgb=o.color||[73,88,107], lh=size*1.5, x=this.mL+16, w=this.W-this.mL-this.mR-16;
  var lines=this._wrap(txt,size,false,w);
  for(var i=0;i<lines.length;i++){ if(i===0){ this.cur.ops.push('0.11 0.72 0.85 rg'); this.cur.ops.push((this.mL+4).toFixed(2)+' '+(this.y-size+3).toFixed(2)+' 3.4 3.4 re f'); } this._textAbs(x,this.y-size,lines[i],size,false,rgb); this.y-=lh; }
};
DTPDF.prototype.gap=function(h){ this.y-=(h||10); };

// chips row (marcas/clientes)
DTPDF.prototype.chips=function(items,o){
  o=o||{}; var size=o.size||11, padx=13, pady=9, gap=9, x=this.mL, maxx=this.W-this.mR, rowH=size+pady*2;
  var startY=this.y;
  for(var i=0;i<items.length;i++){
    var tw=this._tw(items[i],size,true), cw=tw+padx*2;
    if(x+cw>maxx){ x=this.mL; this.y-=rowH+gap; }
    // chip bg
    this.rect(x,this.y-rowH,cw,rowH,o.bg||[247,251,254]);
    this.rectStroke(x,this.y-rowH,cw,rowH,o.border||[215,226,238],1);
    this._textAbs(x+padx,this.y-rowH+pady+1,items[i],size,true,o.color||[10,42,82]);
    x+=cw+gap;
  }
  this.y-=rowH+ (o.after!=null?o.after:6);
};

DTPDF.prototype.bytes=function(){ var s=this._out(); var arr=new Uint8Array(s.length); for(var i=0;i<s.length;i++) arr[i]=s.charCodeAt(i)&0xff; return arr; };
DTPDF.prototype._out=function(){
  var enc=function(s){ return s.length; }; // 1 char = 1 byte (latin1)
  var objs=[]; var nPages=this.pages.length;
  var imgNames=Object.keys(this.images);
  // object ids: 1 catalog, 2 pages, per page content+page, fonts F1 F2, then images
  var id=3; var contentIds=[], pageIds=[];
  for(var i=0;i<nPages;i++){ contentIds.push(id++); pageIds.push(id++); }
  var f1=id++, f2=id++;
  var imgIds={}; for(var k=0;k<imgNames.length;k++){ imgIds[imgNames[k]]=id++; }
  function obj(n,b){ objs[n]=b; }
  obj(1,'<< /Type /Catalog /Pages 2 0 R >>');
  obj(2,'<< /Type /Pages /Count '+nPages+' /Kids ['+pageIds.map(function(p){return p+' 0 R';}).join(' ')+'] >>');
  for(var i=0;i<nPages;i++){
    var stream=this.pages[i].ops.join('\n');
    obj(contentIds[i],'<< /Length '+stream.length+' >>\nstream\n'+stream+'\nendstream');
    var xres=''; var used=Object.keys(this.pages[i].imgs);
    if(used.length){ xres=' /XObject << '+used.map(function(n){return '/'+n+' '+imgIds[n]+' 0 R';}).join(' ')+' >>'; }
    obj(pageIds[i],'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 '+this.W.toFixed(2)+' '+this.H.toFixed(2)+'] /Resources << /Font << /F1 '+f1+' 0 R /F2 '+f2+' 0 R >>'+xres+' >> /Contents '+contentIds[i]+' 0 R >>');
  }
  obj(f1,'<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>');
  obj(f2,'<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>');
  for(var k=0;k<imgNames.length;k++){
    var nm=imgNames[k], im=this.images[nm];
    obj(imgIds[nm],'<< /Type /XObject /Subtype /Image /Width '+im.w+' /Height '+im.h+' /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length '+im.data.length+' >>\nstream\n'+im.data+'\nendstream');
  }
  var out='%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n';
  var offsets=[], maxId=objs.length-1;
  for(var n=1;n<=maxId;n++){ if(objs[n]==null) continue; offsets[n]=out.length; out+=n+' 0 obj\n'+objs[n]+'\nendobj\n'; }
  var xrefPos=out.length, count=maxId+1;
  out+='xref\n0 '+count+'\n0000000000 65535 f \n';
  for(var n=1;n<=maxId;n++){ if(objs[n]==null){ out+='0000000000 65535 f \n'; continue; } var o=offsets[n].toString(); while(o.length<10) o='0'+o; out+=o+' 00000 n \n'; }
  out+='trailer\n<< /Size '+count+' /Root 1 0 R >>\nstartxref\n'+xrefPos+'\n%%EOF';
  return out;
};

// ============================================================================
// Builder: reproduce reference proposal design
// ============================================================================
function buildProposalPDF(d, IMG){
  var NAVY=[10,31,68], DARK=[6,20,44], CY=[22,184,216], INK=[65,84,110], MUT=[110,122,140], LINE=[225,232,241], SOFT=[248,251,255];
  var p=new DTPDF(IMG||{});

  // ---------- helper to start a content page with letterhead ----------
  function contentPage(first){
    if(!first) p.addPage();
    p.imageFullWidthTop('header');
    p.imageFullWidthBottom('footer');
    p.y=p.H-p.mT;
  }
  // Zona segura sobre la barra inferior (footer). Alto real del footer + margen.
  var _footIm=(IMG&&IMG['footer'])?IMG['footer']:null;
  var footH=_footIm?(p.W*_footIm.h/_footIm.w):100;
  var footLimit=footH+18;
  // Si el bloque de alto h no cabe sobre el footer, abre una pagina nueva (con membrete).
  function needSpace(h){ if(p.y-h < footLimit){ contentPage(false); return true; } return false; }
  function fitWrappedText(txt,w,o){
    o=o||{};
    var size=o.size||11, minSize=o.minSize||7, bold=!!o.bold, ls=o.ls||0, maxLines=o.maxLines||99;
    var lines=p._wrap(txt,size,bold,w,ls);
    while(lines.length>maxLines && size>minSize){
      size=Math.max(minSize,size-.5);
      lines=p._wrap(txt,size,bold,w,ls);
    }
    return {lines:lines,size:size,ls:ls};
  }
  function drawWrappedText(x,y,w,txt,o){
    o=o||{};
    var fit=fitWrappedText(txt,w,o), lh=fit.size*(o.lhFactor||1.25), align=o.align||'left';
    for(var i=0;i<fit.lines.length;i++){
      var line=fit.lines[i], tx=x, lineW=p._tw(line,fit.size,!!o.bold,fit.ls);
      if(align==='center') tx=x+(w-lineW)/2;
      else if(align==='right') tx=x+w-lineW;
      p._textAbs(tx,y-i*lh,line,fit.size,!!o.bold,o.color||INK,fit.ls);
    }
    return {bottomY:y-fit.lines.length*lh,lines:fit.lines,size:fit.size};
  }
  function drawBackContactRow(label,value,y,w){
    var x=(p.W-w)/2;
    var labelFit=drawWrappedText(x,y,w,String(label||'').toUpperCase(),{size:9,minSize:8,maxLines:1,bold:true,color:[127,216,236],ls:1,align:'center',lhFactor:1.2});
    var valueFit=drawWrappedText(x,labelFit.bottomY-2,w,value,{size:12,minSize:7,maxLines:4,color:[207,224,244],align:'center',lhFactor:1.2});
    return valueFit.bottomY-8;
  }
  // ---------- card with title/desc/range/bullets, fixed x/width, returns bottom y ----------
  function serviceCard(x,y,w,name,desc,range,bullets){
    var pad=9, innerW=w-pad*2, cy=y-pad;
    // measure
    var hName=14;
    var descH=desc?p.textHeight(desc,10.5,false,innerW,13.5):0;
    var rangeLines=range?p._wrap(range,8.5,true,innerW-16):[];
    var rangeH=range?(rangeLines.length*11+10):0;
    var bulH=0; for(var i=0;i<(bullets||[]).length;i++){ bulH+=p._wrap(bullets[i],10,false,innerW-12).length*12.5; }
    var total=pad+hName+5+descH+ (range?6+rangeH:0) + (bullets&&bullets.length?6+bulH:0) +pad+9;
    // bg
    p.rect(x,y-total,w,total,SOFT); p.rectStroke(x,y-total,w,total,LINE,1);
    // cyan dot + name
    p.rect(x+pad,cy-10,6,6,CY);
    p._textAbs(x+pad+12,cy-11,name,12,true,NAVY);
    var yy=cy-hName-5;
    if(desc){ yy=p.textBox(x+pad,yy-10.5,innerW,desc,{size:10.5,color:INK,lh:13.5})+0; }
    if(range){
      yy-=6; var rh=rangeLines.length*11+10;
      p.rect(x+pad,yy-rh,innerW,rh,[238,250,255]); p.rectStroke(x+pad,yy-rh,innerW,rh,[205,238,247],1);
      var ry=yy-11;
      for(var i=0;i<rangeLines.length;i++){ p._textAbs(x+pad+8,ry,rangeLines[i],8.5,true,[7,111,150]); ry-=11; }
      yy-=rh;
    }
    if(bullets&&bullets.length){
      yy-=6;
      for(var i=0;i<bullets.length;i++){
        var bl=p._wrap(bullets[i],10,false,innerW-12);
        for(var j=0;j<bl.length;j++){ if(j===0){ p.rect(x+pad+2,yy-10+3,3,3,CY);} p._textAbs(x+pad+10,yy-10,bl[j],10,false,INK); yy-=12.5; }
      }
    }
    return y-total;
  }
  function measureServiceCard(w,name,desc,range,bullets){
    var pad=9, innerW=w-pad*2;
    var descH=desc?p.textHeight(desc,10.5,false,innerW,13.5):0;
    var rangeH=range?(p._wrap(range,8.5,true,innerW-16).length*11+10):0;
    var bulH=0; for(var i=0;i<(bullets||[]).length;i++){ bulH+=p._wrap(bullets[i],10,false,innerW-12).length*12.5; }
    return pad+14+5+descH+(range?6+rangeH:0)+(bullets&&bullets.length?6+bulH:0)+pad+9;
  }
  function simpleCard(x,y,w,title,desc,lab){
    var pad=11, innerW=w-pad*2;
    var tLines=p._wrap(title,12,true,innerW); var titleH=tLines.length*15;
    var dLines=desc?p._wrap(desc,10.5,false,innerW):[]; var descH=dLines.length*14;
    var total=pad+(lab?15:0)+titleH+descH+pad;
    p.rect(x,y-total,w,total,SOFT); p.rectStroke(x,y-total,w,total,LINE,1);
    var yy=y-pad;
    if(lab){ p._textAbs(x+pad,yy-9,lab.toUpperCase(),8.5,true,CY,1); yy-=15; }
    for(var t=0;t<tLines.length;t++){ p._textAbs(x+pad,yy-12,tLines[t],12,true,NAVY); yy-=15; }
    for(var t2=0;t2<dLines.length;t2++){ p._textAbs(x+pad,yy-11,dLines[t2],10.5,false,INK); yy-=14; }
    return total;
  }
  function measureSimpleCard(w,title,desc,lab){
    var pad=11, innerW=w-pad*2;
    var titleH=p._wrap(title,12,true,innerW).length*15;
    var descH=desc?p._wrap(desc,10.5,false,innerW).length*14:0;
    return pad+(lab?15:0)+titleH+descH+pad;
  }

  // white logo card helper (contain-fit, centered)
  function logoCard(x,y,w,h,imgName){
    p.rect(x,y-h,w,h,[255,255,255]); p.rectStroke(x,y-h,w,h,[228,237,245],1.2);
    var im=IMG[imgName]; if(!im) return;
    var padX=w*0.15, padY=h*0.20, availW=w-padX*2, availH=h-padY*2;
    var s=Math.min(availW/im.w, availH/im.h);
    var iw=im.w*s, ih=im.h*s;
    p.image(imgName, x+(w-iw)/2, y-h+(h-ih)/2, iw, ih);
  }

  // ======================= PAGE 1: COVER =======================
  p.rect(0,0,p.W,p.H,NAVY);
  // logo top-left (sin recuadro: el fondo del logo coincide con el azul de la portada)
  if(IMG['coverLogo']){ var cl=IMG['coverLogo']; var lw=210, lh=lw*cl.h/cl.w; p.image('coverLogo',56,p.H-46-lh,lw,lh); }
  // site id right
  p._textAbs(p.W-56-p._tw('WWW.DIGITALTECHCOLOMBIA.COM',9,true,1),p.H-52,'WWW.DIGITALTECHCOLOMBIA.COM',9,true,[159,182,214],1);
  p._textAbs(p.W-56-p._tw('BOGOTA D.C.',9,false,1),p.H-66,'BOGOTA D.C.',9,false,[159,182,214],1);
  p._textAbs(p.W-56-p._tw('NIT 900399875-5',9,false,1),p.H-80,'NIT 900399875-5',9,false,[159,182,214],1);
  // frame + title
  var fx=56, fy=p.H-235, fw=p.W-56-120, fh=210;
  p.rectStroke(fx,fy-fh,fw,fh,[255,255,255],2.4);
  p._textAbs(fx+34,fy-58,'PROPUESTA',50,true,[255,255,255]);
  p._textAbs(fx+34,fy-100,'COMERCIAL',32,true,[223,233,247]);
  p._textAbs(fx+34,fy-150,String(d.anio||'2026'),40,false,[127,216,236]);
  p._textAbs(fx+34,fy-180,'ID No. '+(d.consecutivo||'DT-2026-0001'),12,false,[159,182,214],2);
  // footer blocks
  var by=150;
  p._textAbs(56,by-2,'PROPUESTA PARA:',12,true,[127,216,236],2);
  var clientFit=drawWrappedText(56,by-24,210,d.cliente||'Cliente',{size:13,minSize:8,maxLines:4,bold:true,color:[255,255,255],lhFactor:1.16});
  var clientInfoY=clientFit.bottomY-1;
  if(d.contacto){
    var contactFit=drawWrappedText(56,clientInfoY,210,d.contacto,{size:9.5,minSize:7,maxLines:4,color:[199,214,236],lhFactor:1.2});
    clientInfoY=contactFit.bottomY-1;
  }
  if(d.nit) drawWrappedText(56,clientInfoY,210,'NIT '+d.nit,{size:9.5,minSize:7,maxLines:2,color:[159,182,214],lhFactor:1.2});
  var ox=p.W/2+20;
  var organizerW=p.W-p.mR-ox;
  p._textAbs(ox,by-2,'ORGANIZADO POR:',12,true,[127,216,236],2);
  p._textAbs(ox,by-24,'Digital Tech Colombia',15,true,[255,255,255]);
  var organizerY=by-42;
  if(d.comercial){
    var sellerFit=drawWrappedText(ox,organizerY,organizerW,d.comercial,{size:11.5,minSize:7,maxLines:4,color:[199,214,236],lhFactor:1.18});
    organizerY=sellerFit.bottomY-1;
  }
  if(d.comercial_mail) drawWrappedText(ox,organizerY,organizerW,d.comercial_mail,{size:10.5,minSize:7,maxLines:5,color:[159,182,214],lhFactor:1.18});

  // ======================= PAGE 2: SOBRE DIGITAL TECH =======================
  contentPage(false);
  p.kicker('Quienes somos');
  p.h1('Sobre','Digital Tech');
  p.para('Con mas de 15 anos de experiencia en el sector, somos lideres en la integracion de tecnologias que impulsan el crecimiento empresarial. Desde software de gestion empresarial robusto hasta soluciones avanzadas en seguridad informatica, centros de datos y virtualizacion, estamos aqui para optimizar cada aspecto de tu infraestructura TI.',{after:6});
  p.para('Ademas, ofrecemos consultoria especializada en arquitectura empresarial, sistemas de impresion de ultima generacion y herramientas de colaboracion y productividad que conectan y potencian a tu equipo. Nuestro enfoque es simple: entregar soluciones TI completas.',{after:10});
  p.h2('Marcas','aliadas');
  var mcW=(p.W-p.mL-p.mR-24)/3, mY=p.y, mH=64;
  logoCard(p.mL,mY,mcW,mH,'marca_microsoft');
  logoCard(p.mL+mcW+12,mY,mcW,mH,'marca_acronis');
  logoCard(p.mL+2*(mcW+12),mY,mcW,mH,'marca_kyocera');
  var cY=mY-mH-12, cH=80;
  logoCard(p.mL,cY,mcW,cH,'cert_partner_sec');
  logoCard(p.mL+mcW+12,cY,mcW,cH,'cert_partner_mw');
  logoCard(p.mL+2*(mcW+12),cY,mcW,cH,'cert_mct2');
  p.y=cY-cH-14;
  // (Seccion 'Enfoque de la propuesta' eliminada por solicitud del cliente)

  // ======================= PAGE 3: POR QUE ELEGIRNOS =======================
  contentPage(false);
  p.kicker('Confianza');
  p.h1('Por que','elegirnos');
  p.para('Gracias a la pasion, el compromiso y la excelencia de nuestro equipo, hemos forjado relaciones comerciales solidas y fructiferas con algunas de las empresas mas influyentes del sector. Estas alianzas estrategicas reflejan nuestra capacidad para ofrecer resultados excepcionales y valor duradero a nuestros clientes.',{after:8});
  p.h2('Nuestros','clientes');
  var clLogos=['cli_aguas','cli_dimpor','cli_aero','cli_inssa','cli_pepe','cli_carco'];
  var ccW=(p.W-p.mL-p.mR-24)/3, clH=74, clY=p.y;
  for(var i=0;i<clLogos.length;i++){
    var col=i%3, row=Math.floor(i/3);
    logoCard(p.mL+col*(ccW+12), clY-row*(clH+12), ccW, clH, clLogos[i]);
  }
  p.y=clY-2*(clH+12)-2;
  // partner box
  var pbY=p.y, pbH=196, pbW=p.W-p.mL-p.mR;
  p.rect(p.mL,pbY-pbH,pbW,pbH,[244,250,254]); p.rectStroke(p.mL,pbY-pbH,pbW,pbH,[224,238,247],1);
  p._textAbs(p.mL+16,pbY-22,'Microsoft Solutions Partner - Modern Work.',12,true,NAVY);
  p.textBox(p.mL+16,pbY-38,pbW-32,'Somos un socio estrategico para tu negocio, avalado por un equipo altamente calificado y certificado. Nuestra competencia esta reconocida globalmente.',{size:10.5,color:INK,lh:14});
  var badgeImgs=['badge_cyber','badge_ea','badge_azure'];
  var badges=['Cybersecurity Architect','Enterprise Administrator','Azure Solutions Architect'];
  var bsub=['EXPERT','M365 CERTIFIED','EXPERT'];
  var bw=(pbW-32-20)/3, bcardY=pbY-72, bcardH=78;
  for(var i=0;i<3;i++){
    var bx=p.mL+16+i*(bw+10);
    p.rect(bx,bcardY-bcardH,bw,bcardH,[255,255,255]); p.rectStroke(bx,bcardY-bcardH,bw,bcardH,[224,238,247],1);
    var im=IMG[badgeImgs[i]];
    if(im){ var ih=bcardH-14, iw=im.w*ih/im.h; if(iw>bw-16){ iw=bw-16; ih=im.h*iw/im.w; } p.image(badgeImgs[i], bx+(bw-iw)/2, bcardY-7-ih, iw, ih); }
    p._textAbs(bx+2,bcardY-bcardH-16,badges[i],10.5,true,NAVY);
    p._textAbs(bx+2,bcardY-bcardH-30,bsub[i],8.5,true,CY,.5);
  }
  p.y=pbY-pbH-16;
  // (Seccion 'Nuestro valor agregado' eliminada por solicitud del cliente)

  // ======================= PAGE 4: FRENTES =======================
  contentPage(false);
  p.kicker('Propuesta para '+(d.cliente||'el cliente'));
  p.h1('Frentes incluidos y','alcance');
  p.para(d.resumen||'',{size:10.5,color:MUT,lh:15,after:8});
  var svc=d.servicios||[]; var scW=(p.W-p.mL-p.mR-14)/2;
  var colY=[p.y,p.y];
  for(var i=0;i<svc.length;i++){
    var col=i%2; var cx=p.mL+col*(scW+14);
    var h=measureServiceCard(scW,svc[i].name,svc[i].desc,svc[i].range,svc[i].deliverables);
    if(colY[col]-h < footLimit){ /* overflow: nueva pagina sobre el footer */ contentPage(false); colY=[p.y,p.y]; }
    serviceCard(cx,colY[col],scW,svc[i].name,svc[i].desc,svc[i].range,svc[i].deliverables);
    colY[col]-=h+9;
  }

  // ======================= PAGE(S) 5: OFERTAS ECONOMICAS =======================
  // Cada escenario es independiente. Nunca se consolidan ni suman entre sí.
  var props=(d.proposals&&d.proposals.length)?d.proposals:[{
    title:'Oferta economica',
    items:d.items||[],
    monthlySubtotal:d.monthlySubtotal,
    monthlyIva:d.monthlyIva,
    monthlyTotal:d.monthlyTotal,
    contractSubtotal:d.contractSubtotal,
    contractIva:d.contractIva,
    contractTotal:d.contractTotal
  }];
  var cols=[['FRENTE',0.13],['DESCRIPCION',0.22],['CANT.',0.06,'c'],['VALOR UNIT.',0.13,'r'],['DUR.',0.08,'c'],['IVA',0.06,'c'],['MENSUAL',0.15,'r'],['CONTRATO',0.17,'r']];
  var tW=p.W-p.mL-p.mR, xc=p.mL, colX=[]; for(var i=0;i<cols.length;i++){ colX.push(xc); xc+=cols[i][1]*tW; } colX.push(p.W-p.mR);
  function drawEconHeader(){ var hH=26; p.rect(p.mL,p.y-hH,tW,hH,[8,26,50]); for(var i=0;i<cols.length;i++){ var a=cols[i][2]||'l'; var tx=colX[i]+5; if(a==='r') tx=colX[i+1]-5-p._tw(cols[i][0],7.4,true,.25); if(a==='c') tx=colX[i]+ (cols[i][1]*tW)/2 - p._tw(cols[i][0],7.4,true,.25)/2; p._textAbs(tx,p.y-17,cols[i][0],7.4,true,[255,255,255],.25); } p.y-=hH; }
  for(var pidx=0;pidx<props.length;pidx++){
    var PR=props[pidx]||{};
    contentPage(false);
    p.kicker('Inversion');
    if(props.length>1){
      var alternativeLabel='ESCENARIO '+(pidx+1)+' DE '+props.length+(PR.isRecommended?'  |  RECOMENDADO':'');
      p._textAbs(p.mL,p.y-10,alternativeLabel,9,true,PR.isRecommended?CY:MUT,1);
      p.y-=16;
    }
    var proposalTitle=String(PR.title||('Escenario '+(pidx+1)));
    var titleLines=p._wrap(proposalTitle,22,true,tW);
    for(var ttl=0;ttl<titleLines.length;ttl++){ p._textAbs(p.mL,p.y-22,titleLines[ttl],22,true,NAVY); p.y-=27; }
    p.y-=2;
    p.para('Valores tomados de este escenario guardado en la calculadora. El total mensual y el valor del contrato incluyen IVA cuando aplica.',{size:10.5,color:MUT,after:8});
    drawEconHeader();
    var items=PR.items||[];
    for(var r=0;r<items.length;r++){
      var it=items[r]||{};
      var fLines=p._wrap(it.front||'',8.2,true,(colX[1]-colX[0])-9);
      var dLines=p._wrap(it.desc||'',8.4,false,(colX[2]-colX[1])-9);
      var nLines=Math.max(fLines.length,dLines.length);
      var rh=Math.max(30,12+nLines*13);
      if(p.y-rh<footLimit){ contentPage(false); drawEconHeader(); }
      if(r%2===1) p.rect(p.mL,p.y-rh,tW,rh,SOFT);
      for(var j=0;j<fLines.length;j++) p._textAbs(colX[0]+5,p.y-18-j*12,fLines[j],8.2,true,NAVY);
      for(var j=0;j<dLines.length;j++) p._textAbs(colX[1]+5,p.y-18-j*12,dLines[j],8.4,false,INK);
      var cxt=String(it.qty||0); p._textAbs(colX[2]+(cols[2][1]*tW)/2-p._tw(cxt,8.2,false)/2,p.y-18,cxt,8.2,false,INK);
      var unitPrice=String(it.unitPrice||'$0'); p._textAbs(colX[4]-5-p._tw(unitPrice,8.2,false),p.y-18,unitPrice,8.2,false,INK);
      var pxt=String(it.months||0)+' m'; p._textAbs(colX[4]+(cols[4][1]*tW)/2-p._tw(pxt,8.2,false)/2,p.y-18,pxt,8.2,false,INK);
      var ivt=it.iva?'Si':'No'; p._textAbs(colX[5]+(cols[5][1]*tW)/2-p._tw(ivt,8.2,true)/2,p.y-18,ivt,8.2,true,it.iva?[22,150,90]:[150,100,100]);
      var monthlyTotal=String(it.monthlyTotal||'$0'); p._textAbs(colX[7]-5-p._tw(monthlyTotal,8.2,true),p.y-18,monthlyTotal,8.2,true,NAVY);
      var contractTotal=String(it.contractTotal||'$0'); p._textAbs(colX[8]-5-p._tw(contractTotal,8.2,true),p.y-18,contractTotal,8.2,true,NAVY);
      p.line(p.mL,p.y-rh,p.W-p.mR,p.y-rh,LINE,.7);
      p.y-=rh;
    }
    if(items.length===0) p.para('Este escenario no tiene lineas economicas disponibles.',{size:10.5,color:INK,after:6});
    p.gap(16);

    // Notas aclaratorias, totales mensual/contractual y condiciones, reservados sobre el footer.
    var proposalNotes=d.notas||'Los valores son referenciales y pueden variar segun consumo, alcance final y TRM. Actividades fuera de alcance requieren aprobacion y cotizacion adicional.';
    var proposalNoteLines=p._wrap(proposalNotes,10,false,tW-40);
    var conditionTexts=[
      'Vigencia de la oferta: '+(d.vigencia||'-'),
      'Moneda: '+(d.moneda||'COP'),
      'Forma de pago: '+(d.formaPago||'A definir'),
      'Tiempo de entrega: '+(d.tiempoEntrega||'A definir'),
      'Tiempo de contrato: '+(d.tiempoContrato||'A definir')
    ];
    var conditionRows=conditionTexts.map(function(text){ return p._wrap(text,9.5,false,tW-40); });
    var conditionLineCount=conditionRows.reduce(function(total,row){ return total+row.length; },0);
    var conditionsH=Math.max(76,44+conditionLineCount*12+Math.max(0,conditionRows.length-1)*3);
    var proposalNoteOffset=0;
    while(proposalNoteOffset<proposalNoteLines.length){
      var availableNotesH=p.y-footLimit;
      var maxNoteLines=Math.floor((availableNotesH-34)/13);
      var remainingNoteLines=proposalNoteLines.length-proposalNoteOffset;
      var noteLineCount=Math.min(remainingNoteLines,Math.max(0,maxNoteLines));
      var noteChunkH=Math.max(58,34+noteLineCount*13);
      if(noteLineCount<1 || noteChunkH>availableNotesH){
        contentPage(false);
        continue;
      }
      var proposalNotesY=p.y;
      p.rect(p.mL,proposalNotesY-noteChunkH,tW,noteChunkH,[246,251,255]); p.rect(p.mL,proposalNotesY-noteChunkH,5,noteChunkH,CY);
      p._textAbs(p.mL+16,proposalNotesY-22,proposalNoteOffset===0?'Notas aclaratorias':'Notas aclaratorias (continuacion)',12,true,NAVY);
      for(var pnl=0;pnl<noteLineCount;pnl++) p._textAbs(p.mL+16,proposalNotesY-40-pnl*13,proposalNoteLines[proposalNoteOffset+pnl],10,false,INK);
      proposalNoteOffset+=noteLineCount;
      p.y=proposalNotesY-noteChunkH-14;
      if(proposalNoteOffset<proposalNoteLines.length) contentPage(false);
    }
    needSpace(110+18+conditionsH+8);
    var twoY=p.y, totalsGap=14, totalsW=(tW-totalsGap)/2, notesH=110;
    function drawTotalsCard(x,title,subtotal,iva,total){
      subtotal=String(subtotal||'$0'); iva=String(iva||'$0'); total=String(total||'$0');
      p.rectStroke(x,twoY-notesH,totalsW,notesH,[219,231,241],1);
      p._textAbs(x+14,twoY-20,title,12,true,NAVY);
      p._textAbs(x+14,twoY-44,'Subtotal',10.5,false,INK); p._textAbs(x+totalsW-14-p._tw(subtotal,10.5,true),twoY-44,subtotal,10.5,true,NAVY);
      p.line(x+10,twoY-55,x+totalsW-10,twoY-55,LINE,.7);
      p._textAbs(x+14,twoY-69,'IVA',10.5,false,INK); p._textAbs(x+totalsW-14-p._tw(iva,10.5,true),twoY-69,iva,10.5,true,NAVY);
      p.rect(x,twoY-notesH,totalsW,30,NAVY);
      p._textAbs(x+14,twoY-notesH+10,'Total',12.5,true,[255,255,255]); p._textAbs(x+totalsW-14-p._tw(total,12.5,true),twoY-notesH+10,total,12.5,true,[255,255,255]);
    }
    drawTotalsCard(p.mL,'Venta mensual',PR.monthlySubtotal,PR.monthlyIva,PR.monthlyTotal);
    drawTotalsCard(p.mL+totalsW+totalsGap,'Valor del contrato',PR.contractSubtotal,PR.contractIva,PR.contractTotal);
    p.y=twoY-notesH-18;
    var cH=conditionsH; p.rect(p.mL,p.y-cH,tW,cH,[246,251,255]); p.rect(p.mL,p.y-cH,5,cH,CY);
    p._textAbs(p.mL+16,p.y-22,'Condiciones comerciales',12,true,NAVY);
    var cy2=p.y-44;
    for(var ci=0;ci<conditionRows.length;ci++){
      var wrappedCondition=conditionRows[ci];
      for(var cli2=0;cli2<wrappedCondition.length;cli2++){
        if(cli2===0) p.rect(p.mL+16,cy2+3,3,3,CY);
        p._textAbs(p.mL+24,cy2,wrappedCondition[cli2],9.5,false,INK);
        cy2-=12;
      }
      cy2-=3;
    }
  }

  // ======================= PAGE 6: VALORES AGREGADOS =======================
  contentPage(false);
  p.kicker('Cierre consultivo');
  p.h1('Valores agregados y','entregables');
  // Solo se incluyen los valores agregados que el comercial selecciono en el configurador.
  var vaList=[];
  (d.valoresAgregados||[]).slice(0,24).forEach(function(r){
    if(r && r.name){
      vaList.push([
        String(r.name).slice(0,160),
        String(r.detail||'').slice(0,600),
        String(r.front||'General').slice(0,80),
        r.modalidad==='Opcional'?'Opcional':'Incluido'
      ]);
    }
  });
  // Agrupar por frente conservando el orden de aparicion
  var vaGroups={}, vaOrder=[];
  vaList.forEach(function(it){ var f=it[2]||'General'; if(!vaGroups[f]){ vaGroups[f]=[]; vaOrder.push(f); } vaGroups[f].push(it); });
  var fullW=p.W-p.mL-p.mR;
  // Render en formato de lista. Cada linea valida la zona segura para que un texto
  // excepcionalmente largo pueda continuar en otra pagina sin invadir el footer.
  if(vaList.length===0){
    p.para('No se agregaron valores adicionales a esta propuesta.',{size:10.5,color:MUT,lh:15,after:10});
  } else {
    for(var gi=0; gi<vaOrder.length; gi++){
      var gname=vaOrder[gi]; var arr=vaGroups[gname];
      var groupLines=p._wrap(String(gname).toUpperCase(),9.5,true,fullW);
      needSpace(groupLines.length*12+21);
      for(var gl=0;gl<groupLines.length;gl++){
        needSpace(12);
        p._textAbs(p.mL, p.y-11, groupLines[gl], 9.5, true, CY, 1);
        p.y-=12;
      }
      p.y-=6;
      for(var k=0;k<arr.length;k++){
        var it=arr[k];
        var lead=it[0]+(it[3] && it[3]!=='Incluido' ? '  ('+it[3]+')' : '');
        var detail=it[1]||'';
        var leadLines=p._wrap(lead,11.5,true,fullW-18);
        var dLines=detail?p._wrap(detail,10.5,false,fullW-18):[];
        for(var ll=0;ll<leadLines.length;ll++){
          needSpace(15);
          if(ll===0) p.rect(p.mL+3, p.y-11+3, 3.4, 3.4, CY);
          p._textAbs(p.mL+16, p.y-11, leadLines[ll], 11.5, true, NAVY);
          p.y-=15;
        }
        for(var dl=0; dl<dLines.length; dl++){
          needSpace(14);
          p._textAbs(p.mL+16, p.y-10.5, dLines[dl], 10.5, false, MUT);
          p.y-=14;
        }
        if(p.y-6>=footLimit) p.y-=6;
      }
      if(p.y-6>=footLimit) p.y-=6;
    }
  }
  // Entregables + Supuestos (dos columnas) con reserva de espacio
  var ent=['Documento de diseno o alcance tecnico.','Arquitectura o mapa de componentes.','Cronograma ejecutivo.','Actas o evidencias de implementacion.','Transferencia y cierre.'];
  var sup=['Actividades fuera de alcance requieren aprobacion y cotizacion adicional.','Los costos de consumo pueden variar por crecimiento, uso real o TRM.','La implementacion depende de accesos, responsables y ventanas de trabajo.'];
  var cW2=(fullW-28)/2, rx2=p.mL+cW2+28;
  var entH=0; ent.forEach(function(t){ entH+=p._wrap(t,11,false,cW2-12).length*15; });
  var supH=0; sup.forEach(function(t){ supH+=p._wrap(t,11,false,cW2-12).length*15; });
  needSpace(34+Math.max(entH,supH)+12);
  var twoY2=p.y;
  p._textAbs(p.mL,twoY2-14,'Entregables esperados',14,true,NAVY);
  p._textAbs(rx2,twoY2-14,'Supuestos, exclusiones y riesgos',14,true,NAVY);
  var ly=twoY2-34;
  for(var i=0;i<ent.length;i++){ var el=p._wrap(ent[i],11,false,cW2-12); for(var j=0;j<el.length;j++){ if(j===0){p.rect(p.mL+2,ly-11+3,3,3,CY);} p._textAbs(p.mL+10,ly-11,el[j],11,false,INK); ly-=15; } }
  var ry2=twoY2-34;
  for(var i=0;i<sup.length;i++){ var sl=p._wrap(sup[i],11,false,cW2-12); for(var j=0;j<sl.length;j++){ if(j===0){p.rect(rx2+2,ry2-11+3,3,3,CY);} p._textAbs(rx2+10,ry2-11,sl[j],11,false,INK); ry2-=15; } }
  p.y=Math.min(ly,ry2)-14;
  // Las notas ya se presentan una sola vez en la oferta economica.

  // ======================= PAGE 7: OTROS SERVICIOS =======================
  contentPage(false);
  p.kicker('Portafolio');
  p.h1('Otros servicios que','ofrecemos');
  p.para('Ademas de lo cotizado, en Digital Tech contamos con mas soluciones que pueden fortalecer la operacion del cliente. Con gusto podemos ampliar esta propuesta segun su necesidad.',{size:11,color:MUT,lh:15,after:6});
  var cross=d.cross||[]; var xW=(p.W-p.mL-p.mR-14)/2; var xcolY=[p.y,p.y];
  if(cross.length===0){ p.para('La propuesta ya contempla nuestro portafolio principal para este cliente.',{color:INK}); }
  for(var i=0;i<cross.length;i++){
    var col=i%2, cx=p.mL+col*(xW+14);
    var hh=measureSimpleCard(xW,cross[i].name,cross[i].desc);
    if(xcolY[col]-hh<footLimit){ contentPage(false); xcolY=[p.y,p.y]; }
    simpleCard(cx,xcolY[col],xW,cross[i].name,cross[i].desc);
    xcolY[col]-=hh+7;
  }

  // ======================= PAGE 8: BACK COVER =======================
  p.addPage();
  p.rect(0,0,p.W,p.H,NAVY);
  // waves bottom (approx with layered rects)
  p.rect(0,0,p.W,120,[13,58,110]); p.rect(0,0,p.W,70,[22,120,170]); p.rect(0,0,p.W,34,[22,184,216]);
  if(IMG['coverLogo']){ var cl2=IMG['coverLogo']; var lw2=300, lh2=lw2*cl2.h/cl2.w; p.image('coverLogo',(p.W-lw2)/2,p.H-150-lh2,lw2,lh2); }
  p.rect((p.W-74)/2,p.H-210,74,3,[22,184,216]);
  var backW=p.W-112;
  var backNameFit=drawWrappedText(56,p.H-250,backW,d.comercial||'Digital Tech',{size:20,minSize:9,maxLines:4,bold:true,color:[255,255,255],align:'center',lhFactor:1.25});
  var infoY=backNameFit.bottomY-18;
  var rows=[];
  if(d.comercial_tel) rows.push(['Tel:',d.comercial_tel]);
  if(d.comercial_mail) rows.push(['Correo:',d.comercial_mail]);
  rows.push(['Web:','www.digitaltechcolombia.com']);
  for(var i=0;i<rows.length;i++) infoY=drawBackContactRow(rows[i][0],rows[i][1],infoY,backW);
  var foot='Castellana - Forum  |  Cra 45a #95-37  |  Bogota D.C.  |  NIT 900399875-5';
  p._textAbs((p.W-p._tw(foot,9.5,false))/2,infoY-14,foot,9.5,false,[159,182,214]);

  return p.bytes();
}

// Adaptador web V20. Carga los JPEG publicados y conserva el motor JS puro.
(function () {
    "use strict";
    var ASSETS = {
        "header": { url: "/img/proposals/v17/header.jpg", w: 1400, h: 266 },
        "footer": { url: "/img/proposals/v17/footer.jpg", w: 1400, h: 266 },
        "coverLogo": { url: "/img/proposals/v17/coverLogo.jpg", w: 1891, h: 399 },
        "marca_microsoft": { url: "/img/proposals/v17/marca_microsoft.jpg", w: 520, h: 125 },
        "marca_acronis": { url: "/img/proposals/v17/marca_acronis.jpg", w: 520, h: 121 },
        "marca_kyocera": { url: "/img/proposals/v17/marca_kyocera.jpg", w: 520, h: 125 },
        "cert_partner_sec": { url: "/img/proposals/v17/cert_partner_sec.jpg", w: 520, h: 217 },
        "cert_partner_mw": { url: "/img/proposals/v17/cert_partner_mw.jpg", w: 520, h: 214 },
        "cert_mct2": { url: "/img/proposals/v17/cert_mct2.jpg", w: 520, h: 519 },
        "badge_cyber": { url: "/img/proposals/v17/badge_cyber.jpg", w: 520, h: 536 },
        "badge_ea": { url: "/img/proposals/v17/badge_ea.jpg", w: 520, h: 535 },
        "badge_azure": { url: "/img/proposals/v17/badge_azure.jpg", w: 520, h: 535 },
        "cli_aguas": { url: "/img/proposals/v17/cli_aguas.jpg", w: 520, h: 221 },
        "cli_dimpor": { url: "/img/proposals/v17/cli_dimpor.jpg", w: 520, h: 145 },
        "cli_aero": { url: "/img/proposals/v17/cli_aero.jpg", w: 520, h: 195 },
        "cli_inssa": { url: "/img/proposals/v17/cli_inssa.jpg", w: 520, h: 317 },
        "cli_pepe": { url: "/img/proposals/v17/cli_pepe.jpg", w: 520, h: 239 },
        "cli_carco": { url: "/img/proposals/v17/cli_carco.jpg", w: 520, h: 193 }
    };
    var assetPromise = null;

    function toBinaryString(buffer) {
        var bytes = new Uint8Array(buffer);
        var output = "";
        var chunkSize = 32768;
        for (var offset = 0; offset < bytes.length; offset += chunkSize) {
            output += String.fromCharCode.apply(null, bytes.subarray(offset, offset + chunkSize));
        }
        return output;
    }

    async function loadAssets() {
        if (assetPromise) return assetPromise;
        assetPromise = Promise.all(Object.keys(ASSETS).map(async function (name) {
            var definition = ASSETS[name];
            var response = await fetch(definition.url, { credentials: "same-origin" });
            if (!response.ok) throw new Error("No se pudo cargar el recurso " + name + ".");
            return [name, {
                data: toBinaryString(await response.arrayBuffer()),
                w: definition.w,
                h: definition.h
            }];
        })).then(function (entries) {
            var result = {};
            entries.forEach(function (entry) { result[entry[0]] = entry[1]; });
            return result;
        });
        return assetPromise;
    }

    window.generateProposalPdf = async function (data) {
        return buildProposalPDF(data, await loadAssets());
    };
})();
