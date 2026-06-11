(function(global){
  'use strict';
  var S3DOrders=global.S3DOrders=global.S3DOrders||{};

  function start(){
if(window.S3DIcons) S3DIcons.hydrate(document);
    const $=id=>document.getElementById(id);
    const ordersApi=S3DOrders.Api.create();
    let actor=null, reviewOrder=null, clinics=[], orderClinics={};
    let pendingBeforeMinimumDate='';
    let ordersRouter=null, pendingRouteAfterDiscard=null, pendingOrdersRouteError='';
    const OrdersTeeth=S3DOrders.Teeth;
    let rootView, flowView;
    const ordersUiModals=window.S3DModal?{
      find:S3DModal.bind({overlay:$('findOrderPopup'),initialFocus:()=>$('orderFindInput'),selectInitialFocus:true,closeWhenBusy:()=>rootView?rootView.isFinding():false,onClose:()=>{$('findOrderMsg').classList.add('hidden')}}),
      beforeMinimum:S3DModal.bind({overlay:$('beforeMinimumConfirmPopup'),initialFocus:()=>$('beforeMinimumConfirmBackBtn'),onClose:()=>{pendingBeforeMinimumDate=''}}),
      discard:S3DModal.bind({overlay:$('discardOrderFlowPopup'),initialFocus:()=>$('discardOrderFlowBackBtn'),onClose:()=>{pendingRouteAfterDiscard=null}}),
      ordersDay:S3DModal.bind({overlay:$('ordersDayPopup'),initialFocus:()=>$('ordersDayPopupCloseBtn')}),
      cancelOrder:S3DModal.bind({overlay:$('cancelOrderConfirmPopup'),initialFocus:()=>$('cancelOrderConfirmBackBtn'),onClose:()=>{$('cancelOrderConfirmYesBtn').disabled=false;$('cancelOrderConfirmYesBtn').textContent='Yes, cancel order'}})
    }:{};
    function openUiModal(name,overlay,focusTarget){const modal=ordersUiModals[name];if(modal){modal.open();return}S3DDom.setHidden(overlay,false);if(focusTarget)S3DDom.deferFocus(focusTarget)}
    function closeUiModal(name,overlay){const modal=ordersUiModals[name];if(modal)modal.close();else S3DDom.setHidden(overlay,true)}
    function constructionLabel(c){return ({crown:'Crown',bridge:'Bridge',inlayOverlay:'Inlay/Overlay'})[c]||'Construction'}
    function toIsoDate(d){return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`}
    function fdiRangeTeeth(a,b){return OrdersTeeth.range(a,b)}
    function mergeOrderClinics(map){if(!map||typeof map!=='object')return;orderClinics={...orderClinics,...map}}
    function clinicMetaForOrder(o){const code=o?.clinicCode;return code?orderClinics[code]||null:null}
    function normalizeClinicColor(c){const raw=String(c||'').trim();if(/^#[0-9a-fA-F]{6}$/.test(raw))return raw;if(/^#[0-9a-fA-F]{3}$/.test(raw)){const h=raw.slice(1);return `#${h[0]}${h[0]}${h[1]}${h[1]}${h[2]}${h[2]}`}return null}
    function clinicColorForOrder(o){return normalizeClinicColor(o?.clinicDisplayColor||clinicMetaForOrder(o)?.clinicDisplayColor)}
    function clinicDisplayNameForOrder(o){return o?.clinicDisplayName||clinicMetaForOrder(o)?.clinicDisplayName||o?.clinicCode||'Clinic'}
    function clinicSwatchHtml(o){const name=clinicDisplayNameForOrder(o),color=clinicColorForOrder(o),cls=`clinic-swatch${color?'':' clinic-swatch-neutral'}`,style=color?` style="--clinic-color:${color}"`:'';return `<span class="${cls}"${style} title="${esc(name)}"><span class="clinic-swatch-dot" aria-hidden="true"></span><span class="clinic-swatch-label" title="${esc(name)}">${esc(name)}</span></span>`}
    function formatDateBulgarian(iso){if(!iso)return '';const [y,m,d]=iso.split('-');const date=new Date(parseInt(y,10),parseInt(m,10)-1,parseInt(d,10));return new Intl.DateTimeFormat('bg-BG',{day:'2-digit',month:'2-digit',year:'numeric'}).format(date)}
    function formatDateBulgarianWithWeekday(iso){if(!iso)return '';const [y,m,d]=iso.split('-');const date=new Date(parseInt(y,10),parseInt(m,10)-1,parseInt(d,10));const weekday=new Intl.DateTimeFormat('bg-BG',{weekday:'long'}).format(date);const dateStr=formatDateBulgarian(iso);const cap=s=>s.charAt(0).toUpperCase()+s.slice(1);return `${cap(weekday)}, ${dateStr}`}
    function reviewDateCompactMode(){return window.matchMedia('(max-width:900px)').matches&&!!reviewTeeth?.closest('.overview-body')?.classList.contains('overview-body-compact')}
    function formatReviewDeliveryDate(iso){if(!iso)return '';return reviewDateCompactMode()?formatDateBulgarian(iso):formatDateBulgarianWithWeekday(iso)}
    function renderSelectedTeethPreview(container,range,items){S3DOrders.SelectedTeethPreview.render(container,{teeth:range||[],items:items||[],labelPrefix:'Selected teeth',getItemLabel:orderWorkItemLabel})}
    function setOverviewField(el,text){if(!el)return;if('value' in el)el.value=text;else el.textContent=text}
    function setOverviewShade(el,text){if(!el)return;const line=text||'';el.textContent=line;el.classList.toggle('hidden',!line)}
    function syncOverviewBodyLayout(bodyEl,range){if(!bodyEl)return;const count=range?.length||0;bodyEl.classList.toggle('overview-body-compact',count>0&&count<=2)}
    function closeBeforeMinimumConfirmPopup(){closeUiModal('beforeMinimum',beforeMinimumConfirmPopup);pendingBeforeMinimumDate=''}
    function monthForIso(iso){const [y,m]=iso.split('-').map(Number);return new Date(y,m-1,1)}
    function statusText(s){return s==='cancelled'?'Cancelled':s==='created'?'Submitted':(s||'Submitted')}
    function statusIconHtml(s){const cancelled=s==='cancelled';return `<span class="status-icon ${cancelled?'status-cancelled':'status-created'}" title="${statusText(s)}" aria-label="${statusText(s)}">${cancelled?'×':'✓'}</span>`}
    function shadeShort(v){return !v||v==='unspecified'?'—':v}
    function shadeDisplay(v){return !v||v==='unspecified'?'—':v}
    function esc(v){return S3DDom.esc(v)}
    function titleText(v){return String(v||'').replace(/([A-Z])/g,' $1').replace(/^./,c=>c.toUpperCase()).trim()}
    function orderMaterialShort(o){return ({fullContourZirconia:'Zr',pfzLayeredZrCrown:'Layered Zr',pfm:'Metal-ceramic',glassCeramics:'Glass ceramic',pmma:'Temporary PMMA'})[o.material]||titleText(o.material)||'Material'}
    function orderWorkItems(o){return Array.isArray(o.workItems)?o.workItems:[]}
    function orderWorkItemLabel(i){const c=i.constructionType||i.construction||'case';return +i.toothStart===+i.toothEnd?`${constructionLabel(c)} ${i.toothStart||'—'}`:`${constructionLabel(c)} ${i.toothStart||'—'}-${i.toothEnd||'—'}`}
    function orderOverviewBaseText(o){return `${orderMaterialShort(o)} · ${orderWorkItems(o).map(orderWorkItemLabel).join(', ')}`}
    function orderOverviewShadeLine(o){return o.shade&&o.shade!=='unspecified'?`shade ${o.shade}`:''}
    function orderTeethRange(o){const s=new Set();for(const i of orderWorkItems(o)){const start=+i.toothStart,end=+i.toothEnd;const range=Number.isFinite(start)&&Number.isFinite(end)?fdiRangeTeeth(start,end):null;(range||[]).forEach(t=>s.add(t))}return s.size?[...s]:null}
    function syncTopbar(){appChrome.sync(actor)}
    function resetLoginButton(){loginBtn.disabled=false;loginBtn.textContent='Enter'}
    function showAuthenticatedAppShell(){document.body.classList.remove('auth-locked');login.classList.add('hidden');resetLoginButton();syncTopbar()}
    const ordersDirtyGuard=S3DDirtyNavigation.createGuard({isDirty:()=>flowView?flowView.isDirty():false,isSafeTransition:(from,to,navOptions)=>flowView?flowView.isSafeTransition(from,to,navOptions):false,showPrompt:to=>flowView?flowView.promptDiscard({path:to.path}):undefined});
    function openDiscardOrderFlowPopup(targetRoute=null){pendingRouteAfterDiscard=targetRoute;openUiModal('discard',discardOrderFlowPopup,discardOrderFlowBackBtn)}
    function closeDiscardOrderFlowPopup(){closeUiModal('discard',discardOrderFlowPopup);pendingRouteAfterDiscard=null}
    async function ordersBeforeLeave(from,to,navOptions={}){return ordersDirtyGuard.beforeLeave(from,to,navOptions)}
    function showLogin(){document.body.classList.add('auth-locked');login.classList.remove('hidden');list.classList.add('hidden');reviewCard.classList.add('hidden');app.classList.add('hidden');if(rootView){rootView.closeFindOrder();rootView.closeOrdersDay()}closeCancelOrderConfirmPopup();if(flowView){flowView.closeDiscard();flowView.closeBeforeMinimum()}else{closeDiscardOrderFlowPopup();closeBeforeMinimumConfirmPopup()}actor=null;resetLoginButton();syncTopbar()}
    async function loadClinics(){if(!actor?.isLab)return;if(!clinics.length){const result=await ordersApi.clinics();const j=result.data;if(result.ok)clinics=j.items||[]}}
    async function loadOrderByCode(code){const result=await ordersApi.getOrder(code);const j=result.data;if(!result.ok)throw new Error(j.error||'Could not load order.');return j.order}
    function goOrdersRoot(opts){return ordersRouter.navigate('',opts)}
    function goOrderReview(code,opts){return ordersRouter.navigate(`order/${encodeURIComponent(code)}`,opts)}
    function goNewOrder(stepToOpen=1,opts){return ordersRouter.navigate(`new/${stepToOpen}`,opts)}
    function goEditOrder(codeToOpen,stepToOpen=1,opts){return ordersRouter.navigate(`edit/${encodeURIComponent(codeToOpen)}/${stepToOpen}`,opts)}
    async function showReview(codeToOpen){if(!actor)return showLogin();showAuthenticatedAppShell();if(rootView)rootView.closeOrdersDay();closeCancelOrderConfirmPopup();reviewMsg.classList.add('hidden');try{reviewOrder=await loadOrderByCode(codeToOpen)}catch(err){pendingOrdersRouteError=err.message||'Could not load order.';await ordersRouter.replace('',{skipGuard:true});return}mergeOrderClinics(reviewOrder?.clinicCode?{[reviewOrder.clinicCode]:{clinicCode:reviewOrder.clinicCode,clinicDisplayName:reviewOrder.clinicDisplayName,clinicDisplayColor:reviewOrder.clinicDisplayColor}}:null);renderReview(reviewOrder);list.classList.add('hidden');app.classList.add('hidden');reviewCard.classList.remove('hidden');reviewCard.setAttribute('aria-hidden','false');reviewCard.scrollIntoView({block:'start'});reviewBackTopBtn.focus()}
    function closeReview(restartFindHighlight=true){closeCancelOrderConfirmPopup();reviewCard.classList.add('hidden');reviewCard.setAttribute('aria-hidden','true');list.classList.remove('hidden');if(rootView)rootView.restoreFindHighlightAfterReview(restartFindHighlight)}
    if(!window.__reviewDateResizeBound){window.__reviewDateResizeBound=true;window.addEventListener('resize',()=>{if(reviewOrder&&!reviewCard.classList.contains('hidden')){const iso=reviewOrder.requestedDeliveryDate;reviewOverviewDate.value=iso?formatReviewDeliveryDate(iso):''}})}
    function renderReview(o){reviewCode.textContent=o.shortenedOrderCode||o.orderCode||'—';reviewSub.textContent=`${statusText(o.status)}${actor?.isLab?'':` • ${o.clinicDisplayName||o.clinicCode||''}`}`;const reviewClinicMetaEl=$('reviewClinicMeta');if(reviewClinicMetaEl){if(actor?.isLab){reviewClinicMetaEl.classList.remove('hidden');reviewClinicMetaEl.innerHTML=clinicSwatchHtml(o)}else{reviewClinicMetaEl.classList.add('hidden');reviewClinicMetaEl.innerHTML=''}}reviewOverviewText.textContent=orderOverviewBaseText(o);setOverviewShade(reviewOverviewShade,orderOverviewShadeLine(o));reviewCaseName.textContent=o.caseName||'—';reviewExtraNote.textContent=o.notes?`Note: ${o.notes}`:'';reviewExtraNote.classList.toggle('hidden',!o.notes);const cancelled=o.status==='cancelled';reviewEditBtn.disabled=cancelled;reviewCancelBtn.disabled=cancelled;const range=orderTeethRange(o),previewItems=orderWorkItems(o);syncOverviewBodyLayout(reviewTeeth.closest('.overview-body'),range);reviewOverviewDate.value=o.requestedDeliveryDate?formatReviewDeliveryDate(o.requestedDeliveryDate):'';renderSelectedTeethPreview(reviewTeeth,range,previewItems)}
    function editReviewOrder(){if(!reviewOrder||reviewOrder.status==='cancelled')return;if(rootView)rootView.clearFindHighlight();goEditOrder(reviewOrder.orderCode,1)}
    function openCancelOrderConfirmPopup(){if(!reviewOrder||reviewOrder.status==='cancelled')return;const code=reviewOrder.shortenedOrderCode||reviewOrder.orderCode||'—';cancelOrderConfirmText.innerHTML=`Are you sure you want to cancel order <span class="cancel-order-confirm-code">${esc(code)}</span>?`;openUiModal('cancelOrder',cancelOrderConfirmPopup,cancelOrderConfirmBackBtn)}
    function closeCancelOrderConfirmPopup(){closeUiModal('cancelOrder',cancelOrderConfirmPopup);cancelOrderConfirmYesBtn.disabled=false;cancelOrderConfirmYesBtn.textContent='Yes, cancel order'}
    function promptCancelReviewOrder(){if(!reviewOrder||reviewOrder.status==='cancelled')return;openCancelOrderConfirmPopup()}
    async function confirmCancelReviewOrder(){if(!reviewOrder||reviewOrder.status==='cancelled')return;cancelOrderConfirmYesBtn.disabled=true;cancelOrderConfirmYesBtn.textContent='Cancelling…';reviewMsg.classList.add('hidden');const result=await ordersApi.deleteOrder(reviewOrder.orderCode);const j=result.data;if(!result.ok){closeCancelOrderConfirmPopup();reviewMsg.textContent=j.error||'Could not cancel order.';reviewMsg.classList.remove('hidden');return}closeCancelOrderConfirmPopup();reviewOrder=null;if(rootView){rootView.clearFindHighlight();await rootView.reload()}await goOrdersRoot({skipDirtyGuard:true})}
    async function loadMe(){const result=await ordersApi.me();if(!result.ok)return showLogin();actor=result.data;await ordersRouter.refresh()}
    loginBtn.onclick=async()=>{if(loginBtn.disabled)return;await S3DActionButton.run(loginBtn,{busyText:'Signing in…',action:async()=>{loginMsg.classList.add('hidden');try{const result=await ordersApi.login({organizationCode:clinic.value,pin:pin.value});const j=result.data;if(!result.ok){loginMsg.textContent=j.error||'Login failed.';loginMsg.classList.remove('hidden');return}actor=j;loginMsg.classList.add('hidden');await ordersRouter.refresh()}catch{loginMsg.textContent='Login failed.';loginMsg.classList.remove('hidden')}}})};
    [clinic,pin].forEach(el=>el.addEventListener('keydown',e=>{if(e.key==='Enter')loginBtn.click()}));
    reviewBackTopBtn.onclick=()=>goOrdersRoot();reviewCloseTopBtn.onclick=()=>goOrdersRoot();
    reviewEditBtn.onclick=editReviewOrder;
    reviewCancelBtn.onclick=promptCancelReviewOrder;
    cancelOrderConfirmBackBtn.onclick=closeCancelOrderConfirmPopup;
    cancelOrderConfirmYesBtn.onclick=confirmCancelReviewOrder;
    cancelOrderConfirmPopup.onclick=e=>{if(e.target===cancelOrderConfirmPopup)closeCancelOrderConfirmPopup()};
    document.addEventListener('keydown',e=>{if(e.key==='Escape'&&!reviewCard.classList.contains('hidden')){e.preventDefault();goOrdersRoot()}});
    rootView=S3DOrders.RootView.create({api:ordersApi,getActor:()=>actor,loadClinics:loadClinics,openUiModal:openUiModal,closeUiModal:closeUiModal,onOpenOrder:code=>goOrderReview(code),onNewOrder:step=>goNewOrder(step),onShowShell:showAuthenticatedAppShell,onCloseFlowModals:()=>{if(flowView){flowView.closeDiscard();flowView.closeBeforeMinimum()}else{closeDiscardOrderFlowPopup();closeBeforeMinimumConfirmPopup()}},onCloseCancelOrder:closeCancelOrderConfirmPopup,consumeRouteError:()=>{const msg=pendingOrdersRouteError;pendingOrdersRouteError='';return msg}});
    const reviewView=S3DOrders.ReviewView.create({show:showReview,close:closeReview,render:renderReview,edit:editReviewOrder,promptCancel:promptCancelReviewOrder,confirmCancel:confirmCancelReviewOrder,closeCancel:closeCancelOrderConfirmPopup});
    flowView=S3DOrders.FlowView.create({api:ordersApi,getActor:()=>actor,getClinics:()=>clinics,loadClinics:loadClinics,showLogin:showLogin,showAuthenticatedAppShell:showAuthenticatedAppShell,closeFindOrder:()=>rootView&&rootView.closeFindOrder(),closeOrdersDay:()=>rootView&&rootView.closeOrdersDay(),closeCancelOrder:closeCancelOrderConfirmPopup,openUiModal:openUiModal,closeUiModal:closeUiModal,navigate:(path,opts)=>ordersRouter.navigate(path,opts),replace:(path,opts)=>ordersRouter.replace(path,opts),onRouteError:msg=>{pendingOrdersRouteError=msg},onEditSaved:code=>{reviewOrder=null;if(rootView)rootView.markListHighlight(code)}});
    const createdConfirmationView=S3DOrders.CreatedConfirmationView.create({show:code=>flowView.showCreated(code),done:goOrdersRoot,render:()=>flowView.renderFinalOverview()});
    const appChrome=AppChrome.mount($('appChromeMount'),{product:'scheduler',logoSrc:'/images/logo.png',menuButtonClass:'btn',hideMenuWhenSignedOut:true,brandClick:()=>{if(actor)goOrdersRoot()},brandTitle:'Back to orders'});
    appChrome.onLogout(async()=>{await ordersApi.logout();actor=null;rootView.clearSession();showLogin()});
    ordersRouter=HashRouter.createHashRouter({
      routes:[
        {name:'root',pattern:'',handler:()=>actor?rootView.show():showLogin()},
        {name:'orderReview',pattern:'order/:code',handler:ctx=>reviewView.show(ctx.params.code)},
        {name:'newOrder',pattern:'new/:step',handler:ctx=>flowView.showNew(ctx.params.step)},
        {name:'createdOrder',pattern:'created/:code',handler:ctx=>createdConfirmationView.show(ctx.params.code)},
        {name:'editOrder',pattern:'edit/:code/:step',handler:ctx=>flowView.showEdit(ctx.params.code,ctx.params.step)}
      ],
      beforeLeave:ordersBeforeLeave,
      notFound:()=>ordersRouter.replace('',{skipGuard:true}),
      onError:(err)=>{console.error(err);ordersMsg.textContent=err?.message||'Navigation failed.';ordersMsg.className='msg err'}
    });
    ordersRouter.start();
    loadMe();
    
  }

  S3DOrders.Page={start:start};
})(typeof window !== 'undefined' ? window : globalThis);
