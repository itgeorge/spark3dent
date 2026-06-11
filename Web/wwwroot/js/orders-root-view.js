(function(global){
  'use strict';
  var S3DOrders = global.S3DOrders = global.S3DOrders || {};

  function call(fn, args){ return typeof fn === 'function' ? fn.apply(null, args || []) : undefined; }

  function create(options){
    options = options || {};
    return {
      show: function(){ return call(options.show, arguments); },
      reload: function(){ return call(options.reload, arguments); },
      loadMore: function(){ return call(options.loadMore, arguments); },
      setViewMode: function(){ return call(options.setViewMode, arguments); },
      openFindOrder: function(){ return call(options.openFindOrder, arguments); },
      closeFindOrder: function(){ return call(options.closeFindOrder, arguments); },
      openOrdersDay: function(){ return call(options.openOrdersDay, arguments); },
      closeOrdersDay: function(){ return call(options.closeOrdersDay, arguments); },
      clearFindHighlight: function(){ return call(options.clearFindHighlight, arguments); },
      clearSession: function(){ return call(options.clearSession, arguments); }
    };
  }

  S3DOrders.RootView = { create: create };
})(typeof window !== 'undefined' ? window : globalThis);
