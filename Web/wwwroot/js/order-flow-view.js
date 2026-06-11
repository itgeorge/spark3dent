(function(global){
  'use strict';
  var S3DOrders = global.S3DOrders = global.S3DOrders || {};

  function call(fn, args){ return typeof fn === 'function' ? fn.apply(null, args || []) : undefined; }

  function create(options){
    options = options || {};
    return {
      showNew: function(){ return call(options.showNew, arguments); },
      showEdit: function(){ return call(options.showEdit, arguments); },
      showShell: function(){ return call(options.showShell, arguments); },
      reset: function(){ return call(options.reset, arguments); },
      render: function(){ return call(options.render, arguments); },
      goBack: function(){ return call(options.goBack, arguments); },
      goNext: function(){ return call(options.goNext, arguments); },
      requestBackToList: function(){ return call(options.requestBackToList, arguments); },
      isDirty: function(){ return !!call(options.isDirty, arguments); },
      isSafeTransition: function(){ return !!call(options.isSafeTransition, arguments); },
      promptDiscard: function(){ return call(options.promptDiscard, arguments); },
      closeDiscard: function(){ return call(options.closeDiscard, arguments); },
      confirmDiscard: function(){ return call(options.confirmDiscard, arguments); }
    };
  }

  S3DOrders.FlowView = { create: create };
})(typeof window !== 'undefined' ? window : globalThis);
