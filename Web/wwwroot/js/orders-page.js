(function(global){
  'use strict';
  var S3DOrders=global.S3DOrders=global.S3DOrders||{};

  function start(){
if(window.S3DIcons) S3DIcons.hydrate(document);
    const $=id=>document.getElementById(id);
    const ordersApi=S3DOrders.Api.create();
    let actor=null, clinics=[], materialOptions=[];
    let pendingBeforeMinimumDate='';
    let ordersRouter=null, pendingRouteAfterDiscard=null, pendingOrdersRouteError='';
    let rootView, flowView;
    const ordersUiModals=window.S3DModal?{
      find:S3DModal.bind({overlay:$('findOrderPopup'),initialFocus:()=>$('orderFindInput'),selectInitialFocus:true,closeWhenBusy:()=>rootView?rootView.isFinding():false,onClose:()=>{$('findOrderMsg').classList.add('hidden')}}),
      beforeMinimum:S3DModal.bind({overlay:$('beforeMinimumConfirmPopup'),initialFocus:()=>$('beforeMinimumConfirmBackBtn'),onClose:()=>{pendingBeforeMinimumDate=''}}),
      discard:S3DModal.bind({overlay:$('discardOrderFlowPopup'),initialFocus:()=>$('discardOrderFlowBackBtn'),onClose:()=>{pendingRouteAfterDiscard=null}}),
      ordersDay:S3DModal.bind({overlay:$('ordersDayPopup'),initialFocus:()=>$('ordersDayPopupCloseBtn')}),
      cancelOrder:S3DModal.bind({overlay:$('cancelOrderConfirmPopup'),initialFocus:()=>$('cancelOrderConfirmBackBtn'),onClose:()=>{$('cancelOrderConfirmYesBtn').disabled=false;$('cancelOrderConfirmYesBtn').textContent='Да, откажи поръчката'}})
    }:{};
    function openUiModal(name,overlay,focusTarget){const modal=ordersUiModals[name];if(modal){modal.open();return}S3DDom.setHidden(overlay,false);if(focusTarget)S3DDom.deferFocus(focusTarget)}
    function closeUiModal(name,overlay){const modal=ordersUiModals[name];if(modal)modal.close();else S3DDom.setHidden(overlay,true)}
    function closeBeforeMinimumConfirmPopup(){closeUiModal('beforeMinimum',beforeMinimumConfirmPopup);pendingBeforeMinimumDate=''}
    function syncTopbar(){appChrome.sync(actor)}
    function redirectToLogin(){window.location.replace('/login?returnUrl='+encodeURIComponent(window.location.pathname+window.location.search))}
    function showAuthenticatedAppShell(){syncTopbar()}
    const ordersDirtyGuard=S3DDirtyNavigation.createGuard({isDirty:()=>flowView?flowView.isDirty():false,isSafeTransition:(from,to,navOptions)=>flowView?flowView.isSafeTransition(from,to,navOptions):false,showPrompt:to=>flowView?flowView.promptDiscard({path:to.path}):undefined});
    function openDiscardOrderFlowPopup(targetRoute=null){pendingRouteAfterDiscard=targetRoute;openUiModal('discard',discardOrderFlowPopup,discardOrderFlowBackBtn)}
    function closeDiscardOrderFlowPopup(){closeUiModal('discard',discardOrderFlowPopup);pendingRouteAfterDiscard=null}
    async function ordersBeforeLeave(from,to,navOptions={}){return ordersDirtyGuard.beforeLeave(from,to,navOptions)}
    async function loadClinics(){if(!actor?.isLab)return;if(!clinics.length){const result=await ordersApi.clinics();const j=result.data;if(result.ok)clinics=j.items||[]}}
    async function loadMaterialOptions(){if(materialOptions.length)return;const result=await ordersApi.materialOptions();const j=result.data;if(result.ok)materialOptions=j.items||[]}
    function goOrdersRoot(opts){return ordersRouter.navigate('',opts)}
    function goOrderReview(code,opts){return ordersRouter.navigate(`order/${encodeURIComponent(code)}`,opts)}
    function goNewOrder(stepToOpen=1,opts){return ordersRouter.navigate(`new/${stepToOpen}`,opts)}
    function goEditOrder(codeToOpen,stepToOpen=1,opts){return ordersRouter.navigate(`edit/${encodeURIComponent(codeToOpen)}/${stepToOpen}`,opts)}
    function closeCancelOrderConfirmPopup(){closeUiModal('cancelOrder',cancelOrderConfirmPopup);cancelOrderConfirmYesBtn.disabled=false;cancelOrderConfirmYesBtn.textContent='Да, откажи поръчката'}
    async function loadMe(){const result=await ordersApi.me();if(!result.ok)return redirectToLogin();actor=result.data;syncTopbar();ordersRouter.start()}
    rootView=S3DOrders.RootView.create({api:ordersApi,getActor:()=>actor,loadClinics:loadClinics,openUiModal:openUiModal,closeUiModal:closeUiModal,onOpenOrder:code=>goOrderReview(code),onNewOrder:step=>goNewOrder(step),onShowShell:showAuthenticatedAppShell,onCloseFlowModals:()=>{if(flowView){flowView.closeDiscard();flowView.closeBeforeMinimum()}else{closeDiscardOrderFlowPopup();closeBeforeMinimumConfirmPopup()}},onCloseCancelOrder:closeCancelOrderConfirmPopup,consumeRouteError:()=>{const msg=pendingOrdersRouteError;pendingOrdersRouteError='';return msg}});
    const reviewView=S3DOrders.ReviewView.create({api:ordersApi,getActor:()=>actor,showLogin:redirectToLogin,showShell:showAuthenticatedAppShell,closeOrdersDay:()=>rootView&&rootView.closeOrdersDay(),openUiModal:openUiModal,closeUiModal:closeUiModal,replace:(path,opts)=>ordersRouter.replace(path,opts),onRouteError:msg=>{pendingOrdersRouteError=msg},onBack:opts=>goOrdersRoot(opts),onEdit:(code,step)=>goEditOrder(code,step),clearFindHighlight:()=>rootView&&rootView.clearFindHighlight(),restoreFindHighlight:restart=>rootView&&rootView.restoreFindHighlightAfterReview(restart),onCancelled:async()=>{if(rootView){rootView.clearFindHighlight();await rootView.reload()}}});
    flowView=S3DOrders.FlowView.create({api:ordersApi,getActor:()=>actor,getClinics:()=>clinics,getMaterialOptions:()=>materialOptions,loadClinics:loadClinics,loadMaterialOptions:loadMaterialOptions,showLogin:redirectToLogin,showAuthenticatedAppShell:showAuthenticatedAppShell,closeFindOrder:()=>rootView&&rootView.closeFindOrder(),closeOrdersDay:()=>rootView&&rootView.closeOrdersDay(),openOrdersDay:(iso,dayOrders)=>rootView&&rootView.openOrdersDay(iso,dayOrders,{orderClicksEnabled:false}),closeCancelOrder:closeCancelOrderConfirmPopup,openUiModal:openUiModal,closeUiModal:closeUiModal,navigate:(path,opts)=>ordersRouter.navigate(path,opts),replace:(path,opts)=>ordersRouter.replace(path,opts),onRouteError:msg=>{pendingOrdersRouteError=msg},onEditSaved:code=>{if(rootView)rootView.markListHighlight(code)}});
    const createdConfirmationView=S3DOrders.CreatedConfirmationView.create({show:code=>flowView.showCreated(code),done:goOrdersRoot,render:()=>flowView.renderFinalOverview()});
    const appChrome=AppChrome.mount($('appChromeMount'),{product:'scheduler',logoSrc:'/images/logo.png',menuButtonClass:'btn',hideMenuWhenSignedOut:true,brandClick:()=>{if(actor)goOrdersRoot()},brandTitle:'Изход'});
    appChrome.onLogout(async()=>{await ordersApi.logout();actor=null;if(rootView)rootView.clearSession();window.location.replace('/login')});
    ordersRouter=HashRouter.createHashRouter({
      routes:[
        {name:'root',pattern:'',handler:()=>actor?rootView.show():redirectToLogin()},
        {name:'orderReview',pattern:'order/:code',handler:ctx=>reviewView.show(ctx.params.code)},
        {name:'newOrder',pattern:'new/:step',handler:ctx=>flowView.showNew(ctx.params.step)},
        {name:'createdOrder',pattern:'created/:code',handler:ctx=>createdConfirmationView.show(ctx.params.code)},
        {name:'editOrder',pattern:'edit/:code/:step',handler:ctx=>flowView.showEdit(ctx.params.code,ctx.params.step)}
      ],
      beforeLeave:ordersBeforeLeave,
      notFound:()=>ordersRouter.replace('',{skipGuard:true}),
      onError:(err)=>{console.error(err);ordersMsg.textContent=err?.message||'Навигацията не беше успешна.';ordersMsg.className='msg err'}
    });
    loadMe();
    
  }

  S3DOrders.Page={start:start};
})(typeof window !== 'undefined' ? window : globalThis);
