(function(global){
  'use strict';

  var S3DOrders = global.S3DOrders = global.S3DOrders || {};

  function create(options){
    options = options || {};
    var fetchImpl = options.fetch || global.fetch.bind(global);

    function request(url, requestOptions){
      requestOptions = requestOptions || {};
      return fetchImpl(url, {
        headers: Object.assign({ 'content-type': 'application/json' }, requestOptions.headers || {}),
        ...requestOptions
      });
    }

    async function jsonRequest(url, requestOptions, fallback){
      var response = await request(url, requestOptions);
      var data = await response.json().catch(function(){ return fallback || {}; });
      return { ok: response.ok, status: response.status, response: response, data: data };
    }

    function qs(params){
      var search = new URLSearchParams();
      Object.keys(params || {}).forEach(function(key){
        var value = params[key];
        if(value !== undefined && value !== null && value !== '') search.set(key, value);
      });
      return search.toString();
    }

    return {
      request: request,
      jsonRequest: jsonRequest,
      me: function(){ return jsonRequest('/api/scheduling/auth/me'); },
      logout: function(){ return request('/api/scheduling/auth/logout', { method:'POST', body:'{}' }); },
      listOrders: function(options){ var query = qs({ limit: options && options.limit || '50', cursor: options && options.cursor }); return jsonRequest('/api/scheduling/orders?' + query, undefined, { error:'Поръчките не можаха да се заредят.' }); },
      calendarOrders: function(start, end){ return jsonRequest('/api/scheduling/orders/calendar?start=' + encodeURIComponent(start) + '&end=' + encodeURIComponent(end), undefined, { error:'Календарът не можа да се зареди.' }); },
      nonWorkingDays: function(start, end){ return jsonRequest('/api/scheduling/non-working-days?start=' + encodeURIComponent(start) + '&end=' + encodeURIComponent(end), undefined, { error:'Неработните дни не можаха да се заредят.' }); },
      findOrder: function(code, limit){ return jsonRequest('/api/scheduling/orders/find?' + qs({ code: code, limit: limit || '50' }), undefined, { error:'Поръчката не беше намерена.' }); },
      getOrder: function(code){ return jsonRequest('/api/scheduling/orders/' + encodeURIComponent(code), undefined, { error:'Поръчката не можа да се зареди.' }); },
      createOrder: function(payload){ return jsonRequest('/api/scheduling/orders', { method:'POST', body: JSON.stringify(payload || {}) }, { error:'Поръчката не можа да се запази.' }); },
      updateOrder: function(code, payload){ return jsonRequest('/api/scheduling/orders/' + encodeURIComponent(code), { method:'PUT', body: JSON.stringify(payload || {}) }, { error:'Поръчката не можа да се запази.' }); },
      deleteOrder: function(code){ return jsonRequest('/api/scheduling/orders/' + encodeURIComponent(code), { method:'DELETE' }, { error:'Поръчката не можа да се откаже.' }); },
      clinics: function(){ return jsonRequest('/api/scheduling/clinics', undefined, { items: [] }); },
      clinicMembers: function(clinicCode){ return jsonRequest('/api/scheduling/clinics/' + encodeURIComponent(clinicCode) + '/members', undefined, { items: [] }); },
      materialOptions: function(){ return jsonRequest('/api/scheduling/material-options', undefined, { items: [] }); },
      dateAvailability: function(payload){ return jsonRequest('/api/scheduling/dates', { method:'POST', body: JSON.stringify(payload || {}) }); }
    };
  }

  S3DOrders.Api = { create: create };
})(typeof window !== 'undefined' ? window : globalThis);
