(function(global){
  'use strict';
  var S3DOrders = global.S3DOrders = global.S3DOrders || {};

  function esc(v){ return S3DDom.esc(v); }
  function constructionLabel(c){ return ({crown:'Crown',bridge:'Bridge',inlayOverlay:'Inlay/Overlay'})[c] || 'Construction'; }
  function toIsoDate(d){ return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`; }
  function monthForIso(iso){ const [y,m] = iso.split('-').map(Number); return new Date(y, m - 1, 1); }
  function formatDateBulgarian(iso){ if(!iso)return ''; const [y,m,d]=iso.split('-'); const date=new Date(parseInt(y,10),parseInt(m,10)-1,parseInt(d,10)); return new Intl.DateTimeFormat('bg-BG',{day:'2-digit',month:'2-digit',year:'numeric'}).format(date); }
  function formatDateBulgarianWithWeekday(iso){ if(!iso)return ''; const [y,m,d]=iso.split('-'); const date=new Date(parseInt(y,10),parseInt(m,10)-1,parseInt(d,10)); const weekday=new Intl.DateTimeFormat('bg-BG',{weekday:'long'}).format(date); const cap=s=>s.charAt(0).toUpperCase()+s.slice(1); return `${cap(weekday)}, ${formatDateBulgarian(iso)}`; }
  function formatDeliveryShortBg(iso){ if(!iso)return '—'; const [y,m,d]=iso.split('-'); const date=new Date(parseInt(y,10),parseInt(m,10)-1,parseInt(d,10)); return new Intl.DateTimeFormat('bg-BG',{day:'numeric',month:'short'}).format(date).replace(/\.$/,''); }
  function statusText(s){ return s==='cancelled'?'Cancelled':s==='created'?'Submitted':(s||'Submitted'); }
  function statusIconHtml(s){ const cancelled=s==='cancelled'; return `<span class="status-icon ${cancelled?'status-cancelled':'status-created'}" title="${statusText(s)}" aria-label="${statusText(s)}">${cancelled?'×':'✓'}</span>`; }
  function shadeShort(v){ return !v||v==='unspecified'?'—':v; }
  function shadeDisplay(v){ return !v||v==='unspecified'?'—':v; }
  function titleText(v){ return String(v||'').replace(/([A-Z])/g,' $1').replace(/^./,c=>c.toUpperCase()).trim(); }
  function orderMaterialShort(o){ return ({fullContourZirconia:'Zr',pfzLayeredZrCrown:'Layered Zr',pfm:'Metal-ceramic',glassCeramics:'SiLi',pmma:'PMMA-S',pmmaTelio:'PMMA-T'})[o.material] || titleText(o.material) || 'Material'; }
  function orderMaterialCalendarShort(o){ return ({fullContourZirconia:'Zr',pfzLayeredZrCrown:'PFZ',pfm:'M',glassCeramics:'SiLi',pmma:'PMMA-S',pmmaTelio:'PMMA-T'})[o.material] || orderMaterialShort(o); }
  function orderWorkItems(o){ return Array.isArray(o.workItems) ? o.workItems : []; }
  function orderWorkItemLabel(i){ const c=i.constructionType||i.construction||'case'; return +i.toothStart===+i.toothEnd ? `${constructionLabel(c)} ${i.toothStart||'—'}` : `${constructionLabel(c)} ${i.toothStart||'—'}-${i.toothEnd||'—'}`; }
  function orderOverviewBaseText(o){ return `${orderMaterialShort(o)} · ${orderWorkItems(o).map(orderWorkItemLabel).join(', ')}`; }
  function orderOverviewShadeLine(o){ return o.shade&&o.shade!=='unspecified' ? `shade ${o.shade}` : ''; }
  function orderTeethRange(o){ const s=new Set(); const rangeFn=S3DOrders.Teeth.range; for(const i of orderWorkItems(o)){ const start=+i.toothStart,end=+i.toothEnd; const range=Number.isFinite(start)&&Number.isFinite(end)?rangeFn(start,end):null; (range||[]).forEach(t=>s.add(t)); } return s.size?[...s]:null; }
  function normalizeClinicColor(c){ const raw=String(c||'').trim(); if(/^#[0-9a-fA-F]{6}$/.test(raw))return raw; if(/^#[0-9a-fA-F]{3}$/.test(raw)){ const h=raw.slice(1); return `#${h[0]}${h[0]}${h[1]}${h[1]}${h[2]}${h[2]}`; } return null; }
  function clinicMetaForOrder(o, clinicsByCode){ const code=o?.clinicCode; return code && clinicsByCode ? clinicsByCode[code] || null : null; }
  function clinicDisplayNameForOrder(o, clinicsByCode){ return o?.clinicDisplayName || clinicMetaForOrder(o, clinicsByCode)?.clinicDisplayName || o?.clinicCode || 'Clinic'; }
  function clinicColorForOrder(o, clinicsByCode){ return normalizeClinicColor(o?.clinicDisplayColor || clinicMetaForOrder(o, clinicsByCode)?.clinicDisplayColor); }
  function clinicSwatchHtml(o, clinicsByCode){ const name=clinicDisplayNameForOrder(o, clinicsByCode), color=clinicColorForOrder(o, clinicsByCode), cls=`clinic-swatch${color?'':' clinic-swatch-neutral'}`, style=color?` style="--clinic-color:${color}"`:''; return `<span class="${cls}"${style} title="${esc(name)}"><span class="clinic-swatch-dot" aria-hidden="true"></span><span class="clinic-swatch-label" title="${esc(name)}">${esc(name)}</span></span>`; }
  function clinicSwatchDotHtml(o, clinicsByCode){ const name=clinicDisplayNameForOrder(o, clinicsByCode), color=clinicColorForOrder(o, clinicsByCode), cls=`clinic-swatch-dot-only${color?'':' clinic-swatch-neutral'}`, style=color?` style="--clinic-color:${color}"`:''; return `<span class="${cls}"${style} title="${esc(name)}" aria-label="Clinic ${esc(name)}"></span>`; }

  S3DOrders.Format = {
    constructionLabel,
    toIsoDate,
    monthForIso,
    formatDateBulgarian,
    formatDateBulgarianWithWeekday,
    formatDeliveryShortBg,
    statusText,
    statusIconHtml,
    shadeShort,
    shadeDisplay,
    titleText,
    orderMaterialShort,
    orderMaterialCalendarShort,
    orderWorkItems,
    orderWorkItemLabel,
    orderOverviewBaseText,
    orderOverviewShadeLine,
    orderTeethRange,
    normalizeClinicColor,
    clinicMetaForOrder,
    clinicDisplayNameForOrder,
    clinicColorForOrder,
    clinicSwatchHtml,
    clinicSwatchDotHtml
  };
})(typeof window !== 'undefined' ? window : globalThis);
