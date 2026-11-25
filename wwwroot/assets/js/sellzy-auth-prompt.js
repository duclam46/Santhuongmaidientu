(function () {
  "use strict";

  var STORAGE_KEY = "sellzy_pending_return_url";
  var pendingReturnUrl = null;
  var weInjectedReturnParam = false;

  function isAuthenticated() {
    if (window.__sellzySessionAuthenticated === true) return true;
    var modal = document.getElementById("sellzy-auth-prompt-modal");
    return Boolean(modal && modal.getAttribute("data-authenticated") === "true");
  }

  function markSessionAuthenticated() {
    window.__sellzySessionAuthenticated = true;
    var modal = document.getElementById("sellzy-auth-prompt-modal");
    if (modal) {
      modal.setAttribute("data-authenticated", "true");
      modal.classList.remove("is-open");
    }
    var notifBtn = document.getElementById("notification-sidebar-btn");
    if (notifBtn) {
      notifBtn.removeAttribute("data-auth-prompt");
      notifBtn.removeAttribute("data-auth-return-url");
    }
  }
  window.markSellzySessionAuthenticated = markSessionAuthenticated;

  function isSafeLocalUrl(url) {
    return (
      typeof url === "string" &&
      url.charAt(0) === "/" &&
      url.indexOf("//") !== 0 &&
      url.indexOf("/\\") !== 0
    );
  }

  function updateQueryReturnUrl(value) {
    try {
      var params = new URLSearchParams(window.location.search || "");
      if (value) {
        params.set("returnUrl", value);
      } else {
        params.delete("returnUrl");
      }
      var qs = params.toString();
      var nextUrl =
        window.location.pathname +
        (qs ? "?" + qs : "") +
        (window.location.hash || "");
      window.history.replaceState({}, "", nextUrl);
    } catch (e) {
      // ignore
    }
  }

  function syncUrlReturnParam(url) {
    try {
      var params = new URLSearchParams(window.location.search || "");
      if (!params.get("returnUrl")) {
        weInjectedReturnParam = true;
      }
      updateQueryReturnUrl(url);
    } catch (e) {
      // ignore
    }
  }

  function setPendingReturn(url) {
    if (!isSafeLocalUrl(url)) return;
    pendingReturnUrl = url;
    try {
      window.sessionStorage.setItem(STORAGE_KEY, url);
    } catch (e) {
      // ignore
    }
    syncUrlReturnParam(url);
  }

  function getPendingReturn() {
    if (isSafeLocalUrl(pendingReturnUrl)) return pendingReturnUrl;
    try {
      var stored = window.sessionStorage.getItem(STORAGE_KEY);
      if (isSafeLocalUrl(stored)) {
        pendingReturnUrl = stored;
        return stored;
      }
    } catch (e) {
      // ignore
    }
    try {
      var params = new URLSearchParams(window.location.search || "");
      var qUrl = params.get("returnUrl");
      if (isSafeLocalUrl(qUrl)) return qUrl;
    } catch (e) {
      // ignore
    }
    return null;
  }

  function clearPendingReturn() {
    pendingReturnUrl = null;
    try {
      window.sessionStorage.removeItem(STORAGE_KEY);
    } catch (e) {
      // ignore
    }
    if (weInjectedReturnParam) {
      updateQueryReturnUrl(null);
      weInjectedReturnParam = false;
    }
  }

  function closeAnyOpenDrawer() {
    var overlay = document.querySelector(".modal-overlay");
    if (overlay) {
      var activeSel = overlay.getAttribute("data-overlay-for");
      if (activeSel) {
        var activeEl = document.querySelector(activeSel);
        if (activeEl) activeEl.setAttribute("data-state", "close");
      }
      overlay.removeAttribute("data-overlay-for");
      if (window.jQuery) {
        window.jQuery(overlay).stop(true, true).hide();
      } else {
        overlay.style.display = "none";
      }
    }
    var mobileDrawer = document.getElementById("sidebar");
    if (mobileDrawer) mobileDrawer.setAttribute("data-state", "close");
  }

  function clearFormBanners(container) {
    if (!container) return;
    var err = container.querySelector(".auth-error");
    var ok = container.querySelector(".auth-success");
    if (err) {
      err.textContent = "";
      err.classList.add("hidden");
    }
    if (ok) {
      ok.textContent = "";
      ok.classList.add("hidden");
    }
  }

  function openAuthSidebar(selector, keepBanners) {
    var sidebar = document.querySelector(selector);
    if (!sidebar) return;
    closeAnyOpenDrawer();
    if (!keepBanners) clearFormBanners(sidebar);
    sidebar.setAttribute("data-state", "open");
    document.body.classList.add("overflow-hidden", "scrollbar-offset");
    var overlay = document.querySelector(".modal-overlay");
    if (overlay) {
      overlay.setAttribute("data-overlay-for", selector);
      if (window.jQuery) {
        window.jQuery(overlay).stop(true, true).fadeIn();
      } else {
        overlay.style.display = "block";
      }
    }
  }

  var ICON_CHAT =
    '<svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/></svg>';
  var ICON_BELL =
    '<svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.73 21a2 2 0 0 1-3.46 0"/></svg>';
  var ICON_HEART =
    '<svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20.84 4.61a5.5 5.5 0 0 0-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 0 0-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 0 0 0-7.78z"/></svg>';
  var ICON_BOX =
    '<svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"/><polyline points="3.27 6.96 12 12.01 20.73 6.96"/><line x1="12" y1="22.08" x2="12" y2="12"/></svg>';
  var ICON_WALLET =
    '<svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="2" y="5" width="20" height="14" rx="2"/><line x1="2" y1="10" x2="22" y2="10"/></svg>';
  var ICON_USER =
    '<svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>';

  var PRESETS = {
    chat: {
      badge: "Hộp thư Sellzy",
      title: "Đăng nhập để mua sắm và xem tin nhắn",
      desc: "Bạn hãy đăng nhập tài khoản Sellzy để nhắn tin trực tiếp với gian hàng, nhận tư vấn sản phẩm và theo dõi đơn hàng dễ dàng.",
      icon: ICON_CHAT,
      defaultReturnUrl: "/Chat",
      benefits: [
        "Trao đổi trực tiếp với gian hàng theo thời gian thực",
        "Nhận mã giảm giá riêng & kiểm tra tình trạng tồn kho nhanh chóng",
        "Lưu toàn bộ lịch sử mua sắm, tin nhắn và hỗ trợ khiếu nại minh bạch"
      ]
    },
    shop_chat: {
      badge: "Tư vấn cùng Gian hàng",
      title: "Đăng nhập để nhắn tin hỏi Shop",
      desc: "Đăng nhập tài khoản Sellzy để kết nối trực tiếp với gian hàng, hỏi thông tin chi tiết sản phẩm và nhận ưu đãi riêng.",
      icon: ICON_CHAT,
      defaultReturnUrl: "/Chat",
      benefits: [
        "Gửi kèm thẻ sản phẩm, hình ảnh thực tế và hỏi tồn kho ngay lập tức",
        "Nhận voucher giảm giá độc quyền do Shop gửi trong hội thoại",
        "Lưu vết trao đổi rõ ràng giúp bảo vệ quyền lợi khi mua sắm"
      ]
    },
    notifications: {
      badge: "Thông báo hệ thống",
      title: "Đăng nhập để xem thông báo của bạn",
      desc: "Đăng nhập để cập nhật đầy đủ khuyến mãi, biến động đơn hàng và thông báo quan trọng từ hệ thống Sellzy.",
      icon: ICON_BELL,
      defaultReturnUrl: "/Chat/SystemNotifications",
      benefits: [
        "Theo dõi cập nhật trạng thái đơn hàng và hoàn tiền tức thì",
        "Không bỏ lỡ ưu đãi Flash Sale và voucher dành riêng cho bạn",
        "Đồng bộ lịch sử thông báo đã đọc trên mọi thiết bị"
      ]
    },
    wishlist: {
      badge: "Danh sách yêu thích",
      title: "Đăng nhập để lưu & xem sản phẩm yêu thích",
      desc: "Đăng nhập để lưu lại các sản phẩm bạn quan tâm, theo dõi giảm giá nhanh và đồng bộ trên mọi thiết bị.",
      icon: ICON_HEART,
      defaultReturnUrl: "/my-account/wishlist",
      benefits: [
        "Lưu giữ không giới hạn các sản phẩm yêu thích để mua sau",
        "Dễ dàng so sánh giá và theo dõi khuyến mãi từng mặt hàng",
        "Thêm nhanh vào giỏ hàng bất cứ lúc nào khi quay lại"
      ]
    },
    orders: {
      badge: "Đơn hàng của tôi",
      title: "Đăng nhập để theo dõi đơn hàng",
      desc: "Bạn hãy đăng nhập tài khoản Sellzy để xem tiến độ giao hàng, kiểm tra hóa đơn và gửi yêu cầu hỗ trợ khi cần.",
      icon: ICON_BOX,
      defaultReturnUrl: "/my-account/orders",
      benefits: [
        "Theo dõi hành trình giao nhận chi tiết theo thời gian thực",
        "Quản lý hóa đơn, xác nhận nhận hàng và mua lại chỉ với 1 chạm",
        "Mở yêu cầu đổi trả hoặc khiếu nại nhanh khi có phát sinh"
      ]
    },
    wallet: {
      badge: "Ví điện tử Sellzy",
      title: "Đăng nhập để truy cập Ví Sellzy",
      desc: "Đăng nhập để kiểm tra số dư ví, lịch sử hoàn tiền và thanh toán đơn hàng nhanh chóng hơn.",
      icon: ICON_WALLET,
      defaultReturnUrl: "/my-wallet",
      benefits: [
        "Thanh toán đơn hàng tức thì bằng số dư Ví Sellzy an toàn",
        "Nhận tiền hoàn trả khiếu nại nhanh chóng và minh bạch",
        "Tra cứu đầy đủ lịch sử biến động số dư chi tiết"
      ]
    },
    account: {
      badge: "Tài khoản Sellzy",
      title: "Đăng nhập để quản lý tài khoản & mua sắm",
      desc: "Đăng nhập để sử dụng đầy đủ tiện ích mua sắm, tin nhắn gian hàng, sổ địa chỉ và bảo vệ quyền lợi người mua.",
      icon: ICON_USER,
      defaultReturnUrl: "/my-account",
      benefits: [
        "Quản lý hồ sơ cá nhân, sổ địa chỉ nhận hàng và bảo mật tài khoản",
        "Kết nối liền mạch với hộp thư Shop, Ví Sellzy và Đơn hàng",
        "Hưởng đầy đủ chính sách bảo vệ người mua chính hãng 100%"
      ]
    }
  };

  function closeSellzyAuthPrompt(keepBodyLock) {
    var modal = document.getElementById("sellzy-auth-prompt-modal");
    if (modal) modal.classList.remove("is-open");
    if (!keepBodyLock) {
      document.body.classList.remove("overflow-hidden");
    }
  }

  function showSellzyAuthPrompt(options) {
    var opts = typeof options === "string" ? { context: options } : options || {};
    var key = opts.context && PRESETS[opts.context] ? opts.context : "chat";
    var preset = PRESETS[key];
    var targetReturn = isSafeLocalUrl(opts.returnUrl)
      ? opts.returnUrl
      : preset.defaultReturnUrl;

    if (targetReturn) {
      setPendingReturn(targetReturn);
    }

    var modal = document.getElementById("sellzy-auth-prompt-modal");
    if (!modal) {
      openAuthSidebar(".login-page-sidebar");
      return;
    }

    var badgeEl = document.getElementById("sellzy-auth-prompt-badge");
    var titleEl = document.getElementById("sellzy-auth-prompt-title");
    var descEl = document.getElementById("sellzy-auth-prompt-desc");
    var iconEl = document.getElementById("sellzy-auth-prompt-icon");
    var benefitsEl = document.getElementById("sellzy-auth-prompt-benefits");

    if (badgeEl) badgeEl.textContent = opts.badge || preset.badge;
    if (titleEl) titleEl.textContent = opts.title || preset.title;
    if (descEl) descEl.textContent = opts.desc || preset.desc;
    if (iconEl) iconEl.innerHTML = preset.icon;

    if (benefitsEl) {
      benefitsEl.textContent = "";
      var items = preset.benefits || [];
      for (var i = 0; i < items.length; i++) {
        var li = document.createElement("li");
        var check = document.createElement("span");
        check.className = "sellzy-auth-prompt-check";
        check.innerHTML =
          '<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3"><polyline points="20 6 9 17 4 12"/></svg>';
        var txt = document.createElement("span");
        txt.textContent = items[i];
        li.appendChild(check);
        li.appendChild(txt);
        benefitsEl.appendChild(li);
      }
    }

    closeAnyOpenDrawer();
    document.body.classList.add("overflow-hidden");
    modal.classList.add("is-open");
  }

  window.showSellzyAuthPrompt = showSellzyAuthPrompt;
  window.closeSellzyAuthPrompt = closeSellzyAuthPrompt;

  // Capture listeners to coordinate cleanly with main.js handlers
  document.addEventListener(
    "click",
    function (e) {
      var target = e.target;
      if (!target || !target.closest) return;

      // 1. Intercept protected guest navigation links ([data-auth-prompt])
      var promptTrigger = target.closest("[data-auth-prompt]");
      if (promptTrigger && !isAuthenticated()) {
        e.preventDefault();
        e.stopPropagation();
        var ctx = promptTrigger.getAttribute("data-auth-prompt") || "chat";
        var attrRet = promptTrigger.getAttribute("data-auth-return-url");
        var hrefRet = promptTrigger.getAttribute("href");
        var ret = isSafeLocalUrl(attrRet)
          ? attrRet
          : isSafeLocalUrl(hrefRet)
            ? hrefRet
            : undefined;
        showSellzyAuthPrompt({ context: ctx, returnUrl: ret });
        return;
      }

      // 2. Guest wishlist heart click before sending unauthenticated POST
      var wishBtn = target.closest("[data-wishlist-toggle]");
      if (wishBtn && !isAuthenticated()) {
        e.preventDefault();
        e.stopImmediatePropagation();
        showSellzyAuthPrompt({
          context: "wishlist",
          returnUrl: window.location.pathname + window.location.search
        });
        return;
      }

      // 3. Direct sidebar buttons from /Chat Guest Gate ([data-open-auth])
      var directBtn = target.closest("[data-open-auth]");
      if (directBtn) {
        e.preventDefault();
        e.stopPropagation();
        var mode =
          directBtn.getAttribute("data-open-auth") === "register"
            ? "register"
            : "login";
        var gateRet = directBtn.getAttribute("data-auth-return-url");
        if (isSafeLocalUrl(gateRet)) {
          setPendingReturn(gateRet);
        }
        if (mode === "register") {
          openAuthSidebar(".register-page-sidebar");
          setTimeout(function () {
            var fn = document.getElementById("first-name");
            if (fn) fn.focus();
          }, 220);
        } else {
          openAuthSidebar(".login-page-sidebar");
          setTimeout(function () {
            var em = document.getElementById("login-email");
            if (em) em.focus();
          }, 220);
        }
        return;
      }

      // 4. Google OAuth button: honor pendingReturnUrl if present
      var extBtn = target.closest(".external-login-btn");
      if (extBtn) {
        var pending = getPendingReturn();
        if (pending) {
          e.preventDefault();
          e.stopImmediatePropagation();
          var provider = extBtn.getAttribute("data-provider") || "Google";
          clearPendingReturn();
          window.location.href =
            "/auth/external-login?provider=" +
            encodeURIComponent(provider) +
            "&returnUrl=" +
            encodeURIComponent(pending);
          return;
        }
      }

      // 5. Clear stale pendingReturnUrl when clicking header Login/Register or closing auth sidebars
      var authNavBtn = target.closest(".login-page-btn, .register-page-btn");
      if (authNavBtn && !authNavBtn.closest(".sellzy-auth-sidebar")) {
        clearPendingReturn();
        return;
      }

      var closeBtn = target.closest(".close-sidebar-btn");
      if (closeBtn) {
        var sbFor = closeBtn.getAttribute("data-close-sidebar");
        if (sbFor === ".login-page-sidebar" || sbFor === ".register-page-sidebar") {
          clearPendingReturn();
        }
        return;
      }

      if (target.classList && target.classList.contains("modal-overlay")) {
        var ovFor = target.getAttribute("data-overlay-for");
        if (ovFor === ".login-page-sidebar" || ovFor === ".register-page-sidebar") {
          clearPendingReturn();
        }
      }
    },
    true
  );

  // Wire buttons inside #sellzy-auth-prompt-modal
  var modalEl = document.getElementById("sellzy-auth-prompt-modal");
  if (modalEl) {
    var btnLogin = document.getElementById("sellzy-auth-prompt-login-btn");
    var btnRegister = document.getElementById("sellzy-auth-prompt-register-btn");
    var btnLater = document.getElementById("sellzy-auth-prompt-later-btn");
    var btnCloseX = document.getElementById("sellzy-auth-prompt-close-x");

    if (btnLogin) {
      btnLogin.addEventListener("click", function () {
        closeSellzyAuthPrompt(true);
        openAuthSidebar(".login-page-sidebar");
        setTimeout(function () {
          var em = document.getElementById("login-email");
          if (em) em.focus();
        }, 220);
      });
    }

    if (btnRegister) {
      btnRegister.addEventListener("click", function () {
        closeSellzyAuthPrompt(true);
        openAuthSidebar(".register-page-sidebar");
        setTimeout(function () {
          var fn = document.getElementById("first-name");
          if (fn) fn.focus();
        }, 220);
      });
    }

    if (btnLater) {
      btnLater.addEventListener("click", function () {
        closeSellzyAuthPrompt(false);
        clearPendingReturn();
      });
    }

    if (btnCloseX) {
      btnCloseX.addEventListener("click", function () {
        closeSellzyAuthPrompt(false);
        clearPendingReturn();
      });
    }

    modalEl.addEventListener("click", function (e) {
      if (e.target === modalEl) {
        closeSellzyAuthPrompt(false);
        clearPendingReturn();
      }
    });
  }

  document.addEventListener("keydown", function (e) {
    if (e.key === "Escape") {
      var m = document.getElementById("sellzy-auth-prompt-modal");
      if (m && m.classList.contains("is-open")) {
        closeSellzyAuthPrompt(false);
        clearPendingReturn();
      }
    }
  });

  // Handle query flags (?requireLogin=...&returnUrl=...) and auth sidebar state on load
  try {
    var initParams = new URLSearchParams(window.location.search || "");
    var reqLogin = initParams.get("requireLogin");
    var qReturn = initParams.get("returnUrl");

    if (reqLogin && !isAuthenticated()) {
      initParams.delete("requireLogin");
      var cleanedQs = initParams.toString();
      window.history.replaceState(
        {},
        "",
        window.location.pathname +
          (cleanedQs ? "?" + cleanedQs : "") +
          (window.location.hash || "")
      );
      showSellzyAuthPrompt({
        context: reqLogin,
        returnUrl: isSafeLocalUrl(qReturn) ? qReturn : undefined
      });
      weInjectedReturnParam = true;
    } else if (isAuthenticated()) {
      var openLogin = document.querySelector('.login-page-sidebar[data-state="open"]');
      var openReg = document.querySelector('.register-page-sidebar[data-state="open"]');
      var hasVisibleErr =
        document.querySelector(".login-form .auth-error:not(.hidden)") ||
        document.querySelector(".register-form .auth-error:not(.hidden)");
      if ((openLogin || openReg) && !hasVisibleErr) {
        closeAnyOpenDrawer();
        document.body.classList.remove("overflow-hidden", "scrollbar-offset");
      }
      clearPendingReturn();
    } else {
      var loginErrEl = document.querySelector(".login-form form .auth-error");
      var loginSidebar = document.querySelector(".login-page-sidebar");
      if (
        loginErrEl &&
        !loginErrEl.classList.contains("hidden") &&
        (loginErrEl.textContent || "").trim().length > 0 &&
        loginSidebar &&
        loginSidebar.getAttribute("data-state") !== "open"
      ) {
        openAuthSidebar(".login-page-sidebar", true);
      }
    }
  } catch (e) {
    // ignore
  }
})();
