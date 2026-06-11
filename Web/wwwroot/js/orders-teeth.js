(function(global){
  'use strict';

  var S3DOrders = global.S3DOrders = global.S3DOrders || {};
  var FDI_IMAGE_W = 550;
  var FDI_IMAGE_H = 251;
  var CROP_PAD = 10;
  var upper = [18,17,16,15,14,13,12,11,21,22,23,24,25,26,27,28];
  var lower = [48,47,46,45,44,43,42,41,31,32,33,34,35,36,37,38];
  var all = upper.concat(lower);
  var hitBoxes = {
    18:[4,0,42,118],17:[46,0,43,118],16:[89,0,42,118],15:[131,0,27,118],14:[158,0,27,118],13:[185,0,32,118],12:[217,0,26,118],11:[243,0,33,118],
    21:[276,0,31,118],22:[307,0,27,118],23:[334,0,31,118],24:[365,0,28,118],25:[393,0,26,118],26:[419,0,43,118],27:[462,0,42,118],28:[504,0,44,118],
    48:[4,126,42,125],47:[46,126,43,125],46:[89,126,42,125],45:[131,126,27,125],44:[158,126,27,125],43:[185,126,32,125],42:[217,126,26,125],41:[243,126,33,125],
    31:[276,126,31,125],32:[307,126,27,125],33:[334,126,31,125],34:[365,126,28,125],35:[393,126,26,125],36:[419,126,43,125],37:[462,126,42,125],38:[504,126,44,125]
  };

  function jawFor(tooth){
    var n = +tooth;
    if(upper.indexOf(n) >= 0) return upper;
    if(lower.indexOf(n) >= 0) return lower;
    return null;
  }

  function range(start, end){
    var a = +start, b = +end;
    var seq = jawFor(a);
    if(!seq || jawFor(b) !== seq) return null;
    var i = seq.indexOf(a), j = seq.indexOf(b);
    if(i < 0 || j < 0) return null;
    var lo = Math.min(i, j), hi = Math.max(i, j);
    return seq.slice(lo, hi + 1);
  }

  function normalizeRange(start, end){
    var a = +start, b = +end;
    if(!Number.isFinite(a) || !Number.isFinite(b)) return [String(start), String(end)];
    if(a === b) return [String(a), String(b)];
    var seq = jawFor(a);
    if(!seq || jawFor(b) !== seq) return [String(a), String(b)];
    var ia = seq.indexOf(a), ib = seq.indexOf(b);
    if(ia < 0 || ib < 0) return [String(a), String(b)];
    return ia <= ib ? [String(a), String(b)] : [String(b), String(a)];
  }

  function toothAtJawIndex(jaw, idx){
    var seq = jaw === 'upper' ? upper : lower;
    return idx >= 0 && idx < seq.length ? seq[idx] : null;
  }

  function mapToJaw(tooth, jaw){
    if(tooth === '') return '';
    var cur = +tooth;
    if(!Number.isFinite(cur)) return tooth;
    var fromSeq = jawFor(cur);
    if(!fromSeq) return tooth;
    var idx = fromSeq.indexOf(cur);
    if(idx < 0) return tooth;
    var mapped = toothAtJawIndex(jaw, idx);
    return mapped == null ? tooth : String(mapped);
  }

  function cropBounds(nums, padding){
    padding = padding == null ? CROP_PAD : padding;
    if(!nums || !nums.length) return null;
    var minX = Infinity, minY = Infinity, maxX = 0, maxY = 0;
    nums.forEach(function(tooth){
      var box = hitBoxes[tooth];
      if(!box) return;
      var x = box[0], y = box[1], w = box[2], h = box[3];
      if(!w) return;
      minX = Math.min(minX, x);
      minY = Math.min(minY, y);
      maxX = Math.max(maxX, x + w);
      maxY = Math.max(maxY, y + h);
    });
    if(!Number.isFinite(minX)) return null;
    minX = Math.max(0, minX - padding);
    minY = Math.max(0, minY - padding);
    maxX = Math.min(FDI_IMAGE_W, maxX + padding);
    maxY = Math.min(FDI_IMAGE_H, maxY + padding);
    return { x:minX, y:minY, w:maxX-minX, h:maxY-minY };
  }

  S3DOrders.Teeth = {
    FDI_IMAGE_W: FDI_IMAGE_W,
    FDI_IMAGE_H: FDI_IMAGE_H,
    CROP_PAD: CROP_PAD,
    upper: upper,
    lower: lower,
    all: all,
    hitBoxes: hitBoxes,
    jawFor: jawFor,
    range: range,
    normalizeRange: normalizeRange,
    toothAtJawIndex: toothAtJawIndex,
    mapToJaw: mapToJaw,
    cropBounds: cropBounds
  };
})(typeof window !== 'undefined' ? window : globalThis);
