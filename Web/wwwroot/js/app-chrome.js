(function (global) {
  const PRODUCTS = {
    invoicer: { label: "Invoicer", href: "/", icon: "&#128179;" },
    scheduler: { label: "Scheduler / Orders", href: "/orders", icon: "&#128197;" }
  };

  function actorLabel(actor) {
    if (!actor) return "";
    const role = actor.isTechnician ? "Technician" : "Clinic";
    return `${actor.clinicName || actor.clinicCode} / ${actor.credentialLabel} (${role})`;
  }

  function productMenuHtml(activeProduct) {
    return Object.entries(PRODUCTS).map(([key, p]) => {
      const active = key === activeProduct ? " active" : "";
      const current = key === activeProduct ? ' aria-current="page"' : "";
      const hidden = key === "invoicer" ? ' id="appMenuInvoicer"' : "";
      return `<a href="${p.href}" class="app-menu-item${active}" role="menuitem"${current}${hidden}><span>${p.icon}</span><span>${p.label}</span></a>`;
    }).join("");
  }

  function mount(host, options) {
    const {
      product,
      logoSrc = "/images/logo.png",
      brandClick = null,
      brandTitle = null,
      menuButtonClass = "btn",
      hideMenuWhenSignedOut = false,
      extraActions = null
    } = options;
    const activeProduct = product;
    const productMeta = PRODUCTS[product];
    if (!host || !productMeta) throw new Error("AppChrome.mount: host and valid product are required");

    const extraSlot = host.querySelector("[data-app-chrome-extra]");
    host.classList.add("app-chrome-host");
    host.innerHTML = `
<div class="app-chrome">
  <div class="app-chrome-brand${brandClick ? " clickable" : ""}" id="appChromeBrand">
    <img src="${logoSrc}" alt="" class="app-chrome-logo" aria-hidden="true">
    <div class="app-chrome-brand-text">
      <div class="app-chrome-name">Spark3Dent</div>
      <div class="app-chrome-product">${productMeta.label === "Scheduler / Orders" ? "Scheduler" : productMeta.label}</div>
    </div>
  </div>
  <div class="app-chrome-spacer"></div>
  <div class="app-chrome-actions">
    <div class="app-chrome-extra" data-app-chrome-extra-mount></div>
    <div class="app-menu-wrap${hideMenuWhenSignedOut ? " hidden" : ""}" id="appMenuWrap">
      <button type="button" class="${menuButtonClass}" id="btnAppMenu" title="Menu" aria-haspopup="true" aria-expanded="false" aria-label="Menu">&#9776;</button>
      <div class="app-menu" id="appMenu" role="menu">
        ${productMenuHtml(activeProduct)}
        <div class="app-menu-divider" role="separator"></div>
        <div id="appChromeAccount" class="app-menu-account hidden" role="presentation">
          <div class="app-menu-account-label">Account</div>
          <div id="appChromeActor" class="app-menu-actor"></div>
        </div>
        <button type="button" class="app-menu-item app-menu-logout" id="appChromeLogoutBtn" role="menuitem">Logout</button>
      </div>
    </div>
  </div>
</div>`;

    const extraMount = host.querySelector("[data-app-chrome-extra-mount]");
    if (extraSlot) extraMount.appendChild(extraSlot);
    else if (extraActions) extraMount.appendChild(extraActions);

    const refs = {
      brand: host.querySelector("#appChromeBrand"),
      menuWrap: host.querySelector("#appMenuWrap"),
      menuBtn: host.querySelector("#btnAppMenu"),
      account: host.querySelector("#appChromeAccount"),
      actor: host.querySelector("#appChromeActor"),
      logoutBtn: host.querySelector("#appChromeLogoutBtn"),
      invoicerLink: host.querySelector("#appMenuInvoicer")
    };

    let logoutHandler = null;
    let menuOpenHandler = null;

    function closeMenu() {
      refs.menuWrap.classList.remove("open");
      refs.menuBtn.setAttribute("aria-expanded", "false");
    }

    function toggleMenu() {
      const open = refs.menuWrap.classList.toggle("open");
      refs.menuBtn.setAttribute("aria-expanded", open ? "true" : "false");
      if (open && menuOpenHandler) menuOpenHandler();
    }

    function sync(actor) {
      const signedIn = !!actor;
      refs.actor.textContent = actorLabel(actor);
      refs.account.classList.toggle("hidden", !signedIn);
      if (hideMenuWhenSignedOut) refs.menuWrap.classList.toggle("hidden", !signedIn);
      if (refs.invoicerLink) refs.invoicerLink.classList.toggle("hidden", !actor?.isTechnician);
    }

    function onLogout(handler) {
      logoutHandler = handler;
    }

    function onMenuOpen(handler) {
      menuOpenHandler = handler;
    }

    if (brandTitle) refs.brand.setAttribute("title", brandTitle);
    if (brandClick) {
      refs.brand.setAttribute("role", "button");
      refs.brand.setAttribute("tabindex", "0");
      refs.brand.addEventListener("click", brandClick);
      refs.brand.addEventListener("keydown", (e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          brandClick();
        }
      });
    }

    refs.menuBtn.addEventListener("click", (e) => {
      e.stopPropagation();
      toggleMenu();
    });
    refs.logoutBtn.addEventListener("click", async () => {
      closeMenu();
      if (logoutHandler) await logoutHandler();
    });
    document.addEventListener("click", (e) => {
      if (!e.target.closest("#appMenuWrap")) closeMenu();
    });
    document.addEventListener("keydown", (e) => {
      if (e.key === "Escape") closeMenu();
    });

    return { sync, closeMenu, toggleMenu, onLogout, onMenuOpen, refs, actorLabel };
  }

  global.AppChrome = { mount, actorLabel, PRODUCTS };
})(window);
