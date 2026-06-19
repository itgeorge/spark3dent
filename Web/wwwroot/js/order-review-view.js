(function(global){
  'use strict';
  var S3DOrders = global.S3DOrders = global.S3DOrders || {};

  function create(options){
    options = options || {};
    const $ = id => document.getElementById(id);
    const ordersApi = options.api;
    const getActor = options.getActor || (() => null);
    const showLogin = options.showLogin || (() => {});
    const showShell = options.showShell || (() => {});
    const closeOrdersDay = options.closeOrdersDay || (() => {});
    const openUiModal = options.openUiModal || ((name, overlay, focusTarget) => { S3DDom.setHidden(overlay, false); if(focusTarget)S3DDom.deferFocus(focusTarget); });
    const closeUiModal = options.closeUiModal || ((name, overlay) => S3DDom.setHidden(overlay, true));
    const replace = options.replace || (() => {});
    const onBack = options.onBack || (() => {});
    const onEdit = options.onEdit || (() => {});
    const onRouteError = options.onRouteError || (() => {});
    const onCancelled = options.onCancelled || (async () => {});
    const Format = S3DOrders.Format;

    const app = $('app'), list = $('list'), reviewCard = $('reviewCard');
    const reviewMsg = $('reviewMsg'), reviewCode = $('reviewCode'), reviewSub = $('reviewSub'), reviewOverviewText = $('reviewOverviewText'), reviewOverviewShade = $('reviewOverviewShade'), reviewColorNote = $('reviewColorNote'), reviewOverviewDate = $('reviewOverviewDate');
    const reviewCaseName = $('reviewCaseName'), reviewExtraNote = $('reviewExtraNote'), reviewTeeth = $('reviewTeeth'), reviewBackTopBtn = $('reviewBackTopBtn'), reviewCloseTopBtn = $('reviewCloseTopBtn'), reviewEditBtn = $('reviewEditBtn'), reviewCancelBtn = $('reviewCancelBtn');
    const cancelOrderConfirmPopup = $('cancelOrderConfirmPopup'), cancelOrderConfirmText = $('cancelOrderConfirmText'), cancelOrderConfirmBackBtn = $('cancelOrderConfirmBackBtn'), cancelOrderConfirmYesBtn = $('cancelOrderConfirmYesBtn');

    let reviewOrder = null;

    function actor(){return getActor()}
    function esc(v){return S3DDom.esc(v)}
    function setOverviewShade(el,text){if(!el)return;const line=text||'';el.textContent=line;el.classList.toggle('hidden',!line)}
    function setOverviewColorNote(el,text){if(!el)return;const note=(text||'').trim();el.textContent=note?`Color note: ${note}`:'';el.classList.toggle('hidden',!note)}
    function syncOverviewBodyLayout(bodyEl,range){if(!bodyEl)return;const count=range?.length||0;bodyEl.classList.toggle('overview-body-compact',count>0&&count<=2)}
    function reviewDateCompactMode(){return window.matchMedia('(max-width:900px)').matches&&!!reviewTeeth?.closest('.overview-body')?.classList.contains('overview-body-compact')}
    function formatReviewDeliveryDate(iso){if(!iso)return '';return reviewDateCompactMode()?Format.formatDateBulgarian(iso):Format.formatDateBulgarianWithWeekday(iso)}
    function renderSelectedTeethPreview(container,range,items){S3DOrders.SelectedTeethPreview.render(container,{teeth:range||[],items:items||[],labelPrefix:'Selected teeth',getItemLabel:Format.orderWorkItemLabel})}
    async function loadOrderByCode(code){const result=await ordersApi.getOrder(code);const j=result.data;if(!result.ok)throw new Error(j.error||'Could not load order.');return j.order}

    async function show(codeToOpen){
      if(!actor())return showLogin();
      showShell();
      closeOrdersDay();
      closeCancel();
      reviewMsg.classList.add('hidden');
      try{reviewOrder=await loadOrderByCode(codeToOpen)}catch(err){onRouteError(err.message||'Could not load order.');await replace('',{skipGuard:true});return}
      render(reviewOrder);
      list.classList.add('hidden');
      app.classList.add('hidden');
      reviewCard.classList.remove('hidden');
      reviewCard.setAttribute('aria-hidden','false');
      reviewCard.scrollIntoView({block:'start'});
      reviewBackTopBtn.focus();
    }

    function close(restartFindHighlight=true){
      closeCancel();
      reviewCard.classList.add('hidden');
      reviewCard.setAttribute('aria-hidden','true');
      list.classList.remove('hidden');
      if(options.restoreFindHighlight)options.restoreFindHighlight(restartFindHighlight);
    }

    function render(o){
      reviewCode.textContent=o.shortenedOrderCode||o.orderCode||'—';
      reviewSub.textContent=`${Format.statusText(o.status)}${actor()?.isLab?'':` • ${o.clinicDisplayName||o.clinicCode||''}`}`;
      const reviewClinicMetaEl=$('reviewClinicMeta');
      if(reviewClinicMetaEl){
        if(actor()?.isLab){reviewClinicMetaEl.classList.remove('hidden');reviewClinicMetaEl.innerHTML=Format.clinicSwatchHtml(o)}
        else{reviewClinicMetaEl.classList.add('hidden');reviewClinicMetaEl.innerHTML=''}
      }
      reviewOverviewText.textContent=Format.orderOverviewBaseText(o);
      setOverviewShade(reviewOverviewShade,Format.orderOverviewShadeLine(o));
      setOverviewColorNote(reviewColorNote,o.colorNote);
      reviewCaseName.textContent=o.caseName||'—';
      reviewExtraNote.textContent=o.notes?`Note: ${o.notes}`:'';
      reviewExtraNote.classList.toggle('hidden',!o.notes);
      const cancelled=o.status==='cancelled';
      reviewEditBtn.disabled=cancelled;
      reviewCancelBtn.disabled=cancelled;
      const range=Format.orderTeethRange(o),previewItems=Format.orderWorkItems(o);
      syncOverviewBodyLayout(reviewTeeth.closest('.overview-body'),range);
      reviewOverviewDate.value=o.requestedDeliveryDate?formatReviewDeliveryDate(o.requestedDeliveryDate):'';
      renderSelectedTeethPreview(reviewTeeth,range,previewItems);
    }

    function edit(){if(!reviewOrder||reviewOrder.status==='cancelled')return;if(options.clearFindHighlight)options.clearFindHighlight();onEdit(reviewOrder.orderCode,1)}
    function openCancel(){if(!reviewOrder||reviewOrder.status==='cancelled')return;const code=reviewOrder.shortenedOrderCode||reviewOrder.orderCode||'—';cancelOrderConfirmText.innerHTML=`Are you sure you want to cancel order <span class="cancel-order-confirm-code">${esc(code)}</span>?`;openUiModal('cancelOrder',cancelOrderConfirmPopup,cancelOrderConfirmBackBtn)}
    function closeCancel(){closeUiModal('cancelOrder',cancelOrderConfirmPopup);cancelOrderConfirmYesBtn.disabled=false;cancelOrderConfirmYesBtn.textContent='Yes, cancel order'}
    async function confirmCancel(){
      if(!reviewOrder||reviewOrder.status==='cancelled')return;
      cancelOrderConfirmYesBtn.disabled=true;
      cancelOrderConfirmYesBtn.textContent='Cancelling…';
      reviewMsg.classList.add('hidden');
      const result=await ordersApi.deleteOrder(reviewOrder.orderCode);
      const j=result.data;
      if(!result.ok){closeCancel();reviewMsg.textContent=j.error||'Could not cancel order.';reviewMsg.classList.remove('hidden');return}
      closeCancel();
      reviewOrder=null;
      if(options.clearFindHighlight)options.clearFindHighlight();
      await onCancelled();
      await onBack({skipDirtyGuard:true});
    }

    function bind(){
      if(reviewBackTopBtn)reviewBackTopBtn.onclick=()=>onBack();
      if(reviewCloseTopBtn)reviewCloseTopBtn.onclick=()=>onBack();
      if(reviewEditBtn)reviewEditBtn.onclick=edit;
      if(reviewCancelBtn)reviewCancelBtn.onclick=openCancel;
      if(cancelOrderConfirmBackBtn)cancelOrderConfirmBackBtn.onclick=closeCancel;
      if(cancelOrderConfirmYesBtn)cancelOrderConfirmYesBtn.onclick=confirmCancel;
      if(cancelOrderConfirmPopup)cancelOrderConfirmPopup.onclick=e=>{if(e.target===cancelOrderConfirmPopup)closeCancel()};
      document.addEventListener('keydown',e=>{if(e.key==='Escape'&&!reviewCard.classList.contains('hidden')){e.preventDefault();onBack()}});
      if(!window.__reviewDateResizeBound){window.__reviewDateResizeBound=true;window.addEventListener('resize',()=>{if(reviewOrder&&!reviewCard.classList.contains('hidden')){const iso=reviewOrder.requestedDeliveryDate;reviewOverviewDate.value=iso?formatReviewDeliveryDate(iso):''}})}
    }

    bind();

    return {
      show,
      close,
      render,
      edit,
      promptCancel: openCancel,
      confirmCancel,
      closeCancel
    };
  }

  S3DOrders.ReviewView = { create: create };
})(typeof window !== 'undefined' ? window : globalThis);
