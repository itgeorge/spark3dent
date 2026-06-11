(function(global){
  'use strict';
  var S3DOrders = global.S3DOrders = global.S3DOrders || {};

  function call(fn, args){ return typeof fn === 'function' ? fn.apply(null, args || []) : undefined; }

  function create(options){
    options = options || {};
    return {
      show: function(){ return call(options.show, arguments); },
      done: function(){ return call(options.done, arguments); },
      render: function(){ return call(options.render, arguments); }
    };
  }

  S3DOrders.CreatedConfirmationView = { create: create };
})(typeof window !== 'undefined' ? window : globalThis);
