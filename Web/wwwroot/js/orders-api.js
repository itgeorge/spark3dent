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
      login: function(payload){ return jsonRequest('/api/scheduling/auth/login', { method:'POST', body: JSON.stringify(payload || {}) }, { error:'Login failed.' }); },
      logout: function(){ return request('/api/scheduling/auth/logout', { method:'POST', body:'{}' }); },
      listOrders: function(options){ var query = qs({ limit: options && options.limit || '50', cursor: options && options.cursor }); return jsonRequest('/api/scheduling/orders?' + query, undefined, { error:'Could not load orders.' }); },
      calendarOrders: function(start, end){ return jsonRequest('/api/scheduling/orders/calendar?start=' + encodeURIComponent(start) + '&end=' + encodeURIComponent(end), undefined, { error:'Could not load calendar.' }); },
      nonWorkingDays: function(start, end){ return jsonRequest('/api/scheduling/non-working-days?start=' + encodeURIComponent(start) + '&end=' + encodeURIComponent(end), undefined, { error:'Could not load non-working days.' }); },
      findOrder: function(code, limit){ return jsonRequest('/api/scheduling/orders/find?' + qs({ code: code, limit: limit || '50' }), undefined, { error:'Could not find order.' }); },
      getOrder: function(code){ return jsonRequest('/api/scheduling/orders/' + encodeURIComponent(code), undefined, { error:'Could not load order.' }); },
      createOrder: function(payload){ return jsonRequest('/api/scheduling/orders', { method:'POST', body: JSON.stringify(payload || {}) }, { error:'Could not save order.' }); },
      updateOrder: function(code, payload){ return jsonRequest('/api/scheduling/orders/' + encodeURIComponent(code), { method:'PUT', body: JSON.stringify(payload || {}) }, { error:'Could not save order.' }); },
      deleteOrder: function(code){ return jsonRequest('/api/scheduling/orders/' + encodeURIComponent(code), { method:'DELETE' }, { error:'Could not cancel order.' }); },
      listReservations: function(options){ var query = qs({ limit: options && options.limit || '100' }); return jsonRequest('/api/scheduling/reservations?' + query, undefined, { error:'Could not load reservations.' }); },
      getReservation: function(id){ return jsonRequest('/api/scheduling/reservations/' + encodeURIComponent(id), undefined, { error:'Could not load reservation.' }); },
      createReservation: function(payload){ return jsonRequest('/api/scheduling/reservations', { method:'POST', body: JSON.stringify(payload || {}) }, { error:'Could not save reservation.' }); },
      updateReservation: function(id, payload){ return jsonRequest('/api/scheduling/reservations/' + encodeURIComponent(id), { method:'PUT', body: JSON.stringify(payload || {}) }, { error:'Could not save reservation.' }); },
      deleteReservation: function(id){ return jsonRequest('/api/scheduling/reservations/' + encodeURIComponent(id), { method:'DELETE' }, { error:'Could not cancel reservation.' }); },
      promoteReservation: function(id, payload){ return jsonRequest('/api/scheduling/reservations/' + encodeURIComponent(id) + '/promote', { method:'POST', body: JSON.stringify(payload || {}) }, { error:'Could not promote reservation.' }); },
      reservationDateAvailability: function(payload){ return jsonRequest('/api/scheduling/reservations/dates', { method:'POST', body: JSON.stringify(payload || {}) }); },
      clinics: function(){ return jsonRequest('/api/scheduling/clinics', undefined, { items: [] }); },
      materialOptions: function(){ return jsonRequest('/api/scheduling/material-options', undefined, { items: [] }); },
      dateAvailability: function(payload){ return jsonRequest('/api/scheduling/dates', { method:'POST', body: JSON.stringify(payload || {}) }); }
    };
  }

  S3DOrders.Api = { create: create };
})(typeof window !== 'undefined' ? window : globalThis);
