(function(global){
  'use strict';
  var S3DOrders = global.S3DOrders = global.S3DOrders || {};

  function call(fn, args){ return typeof fn === 'function' ? fn.apply(null, args || []) : undefined; }

  function create(options){
    options = options || {};
    return {
      show: function(){ return call(options.show, arguments); },
      close: function(){ return call(options.close, arguments); },
      render: function(){ return call(options.render, arguments); },
      edit: function(){ return call(options.edit, arguments); },
      promptCancel: function(){ return call(options.promptCancel, arguments); },
      confirmCancel: function(){ return call(options.confirmCancel, arguments); },
      closeCancel: function(){ return call(options.closeCancel, arguments); }
    };
  }

  S3DOrders.ReviewView = { create: create };
})(typeof window !== 'undefined' ? window : globalThis);
