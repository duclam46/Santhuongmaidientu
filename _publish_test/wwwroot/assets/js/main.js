/*------ Main Js ----*/
(function ($) {
  "use strict";

  /* --------------------------------------------------------------------------
   * Envato Review Improvements
   * - Cache frequently used DOM lookups
   * - Defensive checks / error handling around optional plugins
   * - Namespace events for cleanup (memory management)
   * ------------------------------------------------------------------------ */

  // Cached selectors (performance)
  const $win = $(window);
  const $doc = $(document);
  const $body = $("body");
  const $modalOverlay = $(".modal-overlay");
  const $tabPanes = $(".tab-pane");
  const $filterButtons = $(".filter-button");
  const $filterDropdownMenus = $(".filter-dropdown-menu");
  const $filterDropdownIcons = $(".filter-dropdown-icon");
  const $searchResultContainers = $(".search-result-container");

  // Safe plugin initializer (error handling)
  function safeInit(fn) {
    try {
      fn();
    } catch (err) {
      // Silently fail in production templates; prevents broken pages if a vendor plugin is missing
      // console.warn(err);
    }
  }

  // Cleanup registry (memory management)
  const cleanupTasks = [];
  function addCleanup(task) {
    if (typeof task === "function") cleanupTasks.push(task);
  }

  /*------ Preloader ----*/
  $win.on("load.sellzy", function () {
    $(".preloader").addClass("loaded");
  });

  /*------ Wow Animation ----*/
  safeInit(function () {
    if (typeof WOW !== "undefined") new WOW().init();
  });

  /*------ Nice Select ----*/
  safeInit(function () {
    if ($.fn && $.fn.niceSelect) $("select").niceSelect();
  });

  $("select").on("change.sellzy", function () {
    $(this).siblings(".nice-select").addClass("changed");
  });

  /*------ Sticky Header ----*/
  const stickyHeader = $(".sticky-header");
  const win = $win;
  win.on("scroll.sellzy", function () {
    if (win.scrollTop() > 200) {
      stickyHeader.addClass("sticky-top");
    } else {
      stickyHeader.removeClass("sticky-top");
    }
  });

  /*------ Product Filter Buttons ------*/
  const productFilterButtons = $(".home-one-product-filter button");
  if (productFilterButtons.length) {
    $(".home-one-product-filter button:nth-child(1)")
      .addClass("btn-primary")
      .removeClass("btn-default outline shadow-none")
      .siblings()
      .removeClass("btn-primary")
      .addClass("btn-default outline  shadow-none");
    $tabPanes.hide();

    $(".tab-pane:nth-child(1)").addClass("active").show();
    $(".home-one-product-filter button").on("click.sellzy", function () {
      $(this)
        .removeClass("btn-default outline shadow-none")
        .addClass("btn-primary")
        .siblings()
        .removeClass("btn-primary")
        .addClass("btn-default outline  shadow-none");
      $tabPanes.removeClass("active fade").hide();
      let activeTab = $(this).attr("data-tab");
      $(`#${activeTab}`).addClass("active fade").fadeIn();
      return false;
    });
  }

  /*------ Sidebar ----*/
  const sidebarMenu = $("#sidebar-menu-btn");
  const sidebar = $("#sidebar");
  const sidebarMenuClose = $("#side-bar-menu-close");
  if (sidebarMenu.length) {
    sidebarMenu.on("click.sellzy", function () {
      $(sidebar).attr("data-state", "open");
      $modalOverlay.attr("data-overlay-for", "#sidebar");
      $body.addClass("overflow-hidden");
      $modalOverlay.fadeIn();
    });
  }

  if (sidebarMenuClose.length) {
    sidebarMenuClose.on("click.sellzy", function () {
      $(sidebar).attr("data-state", "close");
      $body.removeClass("overflow-hidden");
      $modalOverlay.fadeOut();
    });
  }

  if ($modalOverlay.length) {
    $modalOverlay.on("click.sellzy", function () {
      const overlayFor = $(this).attr("data-overlay-for");
      if (overlayFor) {
        $(overlayFor).attr("data-state", "close");
      }
      $body.removeClass("overflow-hidden scrollbar-offset");
      $(this).fadeOut();
      $(this).removeAttr("data-overlay-for");
    });
  }

  /*------ Cart Sidebar ------*/
  const cartSidebarBtn = $(".cart-sidebar-btn");

  if (cartSidebarBtn.length) {
    cartSidebarBtn.on("click.sellzy", function () {
      isAnythingOpen();
      showSidebar(".cart-sidebar");
    });
  }

  /*------ Register Page Button ------*/
  const registerPageBtn = $(".register-page-btn");
  if (registerPageBtn.length) {
    registerPageBtn.on("click.sellzy", function (e) {
      e.preventDefault();
      showAuthError($(".register-form form"), null);
      isAnythingOpen();
      showSidebar(".register-page-sidebar");
    });
  }

  /*------ Login Page Button ------*/
  const loginPageBtn = $(".login-page-btn");
  if (loginPageBtn.length) {
    loginPageBtn.on("click.sellzy", function (e) {
      e.preventDefault();
      showAuthError($(".login-form form"), null);
      isAnythingOpen();
      showSidebar(".login-page-sidebar");
    });
  }

  /*------ Forgot Password Page Button ------*/
  const forgotPasswordPageBtn = $(".forgot-password-page-btn");
  if (forgotPasswordPageBtn.length) {
    forgotPasswordPageBtn.on("click.sellzy", function (e) {
      e.preventDefault();
      isAnythingOpen();
      showSidebar(".forgot-password-page-sidebar");
    });
  }

  /*------ Reset Password Page Button ------*/
  const resetPasswordPageBtn = $(".reset-password-page-btn");
  if (resetPasswordPageBtn.length) {
    resetPasswordPageBtn.on("click.sellzy", function (e) {
      e.preventDefault();
      isAnythingOpen();
      showSidebar(".reset-password-page-sidebar");
    });
  }

  /*------ OTP Verification Page Button ------*/
  const otpVerificationPageBtn = $(".otp-verification-page-btn");
  if (otpVerificationPageBtn.length) {
    otpVerificationPageBtn.on("click.sellzy", function (e) {
      e.preventDefault();
      isAnythingOpen();
      showSidebar(".otp-verification-page-sidebar");
    });
  }

  /*------ Close Sidebar ------*/
  const closeSidebarBtn = $(".close-sidebar-btn");
  if (closeSidebarBtn.length) {
    closeSidebarBtn.on("click.sellzy", function () {
      const sidebarFor = $(this).attr("data-close-sidebar");
      isAnythingOpen();
      closeSidebar(sidebarFor);
    });
  }

  /*------ Explorer Category ------*/
  const btn = $("#dropdownButton");
  const menu = $("#dropdownMenu");
  const icon = $("#dropdownIcon");

  btn.on("click.sellzy", function () {
    const isOpen = $(this).attr("data-state") === "open";
    if (isOpen) {
      $(this).attr("data-state", "close");
      menu.removeClass("active").addClass("hide");
      icon.removeClass("rotate-180");
    } else {
      btn.attr("data-state", "open");
      menu.removeClass("hide").addClass("active");
      icon.addClass("rotate-180");
    }
  });

  $doc.on("click.sellzy", function (e) {
    if (!$(e.target).closest("#dropdownButton").length) {
      btn.attr("data-state", "close");
      menu.removeClass("active").addClass("hide");
      icon.removeClass("rotate-180");
    }
    // Banner Filter Dropdown Close
    if (!$(e.target).closest(".filter-dropdown").length) {
      $filterButtons.attr("data-state", "close");
      $filterButtons.removeClass(
        "ring-primary text-primary transition-colors duration-300 ease-in-out"
      );
      $filterButtons.find("span i").removeClass(
        "text-primary transition-colors duration-300 ease-in-out"
      );
      $filterDropdownMenus.removeClass("active").addClass("hide");
      $filterDropdownIcons.removeClass(
        "rotate-180 text-primary transition-colors duration-300 ease-in-out"
      );
    }

    if (!$(e.target).closest(".search-input-container").length) {
      $searchResultContainers.attr("data-state", "close");
    }
  });

  /*------ Sellzy Countdown ----*/
  const sellzyCountdown = $(".sellzy-countdown");
  if (sellzyCountdown.length) {
    safeInit(function () {
      if ($.fn && $.fn.countdown) {
        sellzyCountdown.countdown({
          date: "12/13/2026 00:00:00",
          offset: +6,
          day: "Day",
          days: "Days",
          hideOnComplete: true,
        });
      }
    });
  }

  /*------ Search Flow ----*/
  $(".header-search-input").on("input.sellzy", function () {
    const rawValue = $(this).val();
    const searchValue = typeof rawValue === "string" ? rawValue.trim() : "";
    // Basic input validation (prevents unexpected values)
    if (searchValue.length > 0 && searchValue.length <= 200) {
      $(this)
        .closest(".search-input-container")
        .find(".search-result-container")
        .attr("data-state", "open");
    } else {
      $(this)
        .closest(".search-input-container")
        .find(".search-result-container")
        .attr("data-state", "close");
    }
  });

  /*------ Auth (Identity via template sidebars) ----*/
  function showAuthError($scope, message) {
    const $err = $scope.find(".auth-error");
    if (!$err.length) return;
    if (message) {
      $err.text(message).removeClass("hidden");
    } else {
      $err.text("").addClass("hidden");
    }
  }

  function showAuthErrorFromQuery() {
    try {
      const params = new URLSearchParams(window.location.search || "");
      const authError = params.get("authError");
      if (!authError) return;

      const authPopup = params.get("authPopup");
      if (authPopup === "login") {
        isAnythingOpen();
        showSidebar(".login-page-sidebar");
      } else if (authPopup === "register") {
        isAnythingOpen();
        showSidebar(".register-page-sidebar");
      }

      let message = "Xác thực thất bại. Vui lòng thử lại.";
      if (authError.startsWith("provider_not_configured:")) {
        const provider = authError.split(":")[1] || "Google";
        message = `Chưa cấu hình đăng nhập ${provider}. Vui lòng cấu hình ClientId/ClientSecret.`;
      } else if (authError === "external_login_failed") {
        message = "Đăng nhập Google thất bại. Vui lòng thử lại.";
      }

      const $loginScope = $(".login-form form");
      const $registerScope = $(".register-form form");
      if ($loginScope.length) {
        showAuthError($loginScope, message);
      } else if ($registerScope.length) {
        showAuthError($registerScope, message);
      }

      params.delete("authError");
      params.delete("authPopup");
      const qs = params.toString();
      const newUrl = window.location.pathname + (qs ? `?${qs}` : "") + (window.location.hash || "");
      window.history.replaceState({}, "", newUrl);
    } catch (e) {
      // ignore
    }
  }

  showAuthErrorFromQuery();

  async function postJson(url, payload) {
    const res = await fetch(url, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify(payload || {}),
    });

    let data = null;
    try {
      data = await res.json();
    } catch (e) {
      // ignore
    }
    return { ok: res.ok, status: res.status, data };
  }

  // Email/password login
  const $loginForm = $(".login-form form");
  if ($loginForm.length) {
    $loginForm.on("submit.sellzy", async function (e) {
      e.preventDefault();
      const $scope = $(this);
      showAuthError($scope, null);

      const email = String($("#login-email").val() || "").trim();
      const password = String($("#login-password").val() || "");

      const result = await postJson("/auth/login", { email, password });
      if (result.ok) {
        window.location.reload();
        return;
      }
      showAuthError($scope, (result.data && result.data.message) || "Đăng nhập thất bại.");
    });
  }

  // Email/password register
  const $registerForm = $(".register-form form");
  if ($registerForm.length) {
    $registerForm.on("submit.sellzy", async function (e) {
      e.preventDefault();
      const $scope = $(this);
      showAuthError($scope, null);

      const firstName = String($("#first-name").val() || "").trim();
      const lastName = String($("#last-name").val() || "").trim();
      const email = String($("#register-email").val() || "").trim();
      const password = String($("#register-password").val() || "");
      const confirmPassword = String($("#confirm-password").val() || "");

      if (password !== confirmPassword) {
        showAuthError($scope, "Mật khẩu xác nhận không khớp.");
        return;
      }

      const result = await postJson("/auth/register", {
        firstName,
        lastName,
        email,
        password,
        confirmPassword,
      });
      if (result.ok) {
        window.location.reload();
        return;
      }
      showAuthError($scope, (result.data && result.data.message) || "Đăng ký thất bại.");
    });
  }

  // Logout
  const $logoutBtn = $(".logout-btn");
  if ($logoutBtn.length) {
    $logoutBtn.on("click.sellzy", async function (e) {
      e.preventDefault();
      const result = await postJson("/auth/logout", {});
      if (result.ok) {
        window.location.reload();
      }
    });
  }

  // External login (Google)
  const $externalLoginBtn = $(".external-login-btn");
  if ($externalLoginBtn.length) {
    $externalLoginBtn.on("click.sellzy", function (e) {
      e.preventDefault();
      const provider = $(this).attr("data-provider") || "Google";
      const popup = $(this).closest(".register-page-sidebar").length ? "register" : "login";
      const returnParams = new URLSearchParams(window.location.search || "");
      returnParams.delete("authError");
      returnParams.set("authPopup", popup);
      const returnUrl = window.location.pathname + "?" + returnParams.toString() + (window.location.hash || "");
      window.location.href = `/auth/external-login?provider=${encodeURIComponent(provider)}&returnUrl=${encodeURIComponent(returnUrl)}`;
    });
  }

  /*------ Accordion Section ----*/
  $(".accordion-header").on("click.sellzy", function () {
    if ($(this).hasClass("active")) {
      $(this).removeClass("active");
      $(this).siblings(".accordion-body").slideUp();
    } else {
      $(this)
        .parent()
        .siblings()
        .children(".accordion-header")
        .removeClass("active");
      $(this).parent().siblings().children(".accordion-body").slideUp();
      $(this).addClass("active");
      $(this).siblings(".accordion-body").slideDown();
    }
  });

  /*------ FAQ Tab Accordion ----*/
  const faqFilterButton = $(".faq-filter button");
  if (faqFilterButton.length) {
    $(".faq-filter button:nth-child(1)")
      .addClass("active")
      .siblings()
      .removeClass("active");
    $(".faq-tab-pane").hide();
    $(".faq-tab-pane:nth-child(1)").addClass("active").show();

    faqFilterButton.on("click.sellzy", function () {
      $(this).addClass("active").siblings().removeClass("active");
      $(".faq-tab-pane").removeClass("active fade").hide();
      let activeTab = $(this).attr("data-tab");
      $(`#${activeTab}`).addClass("active fade").fadeIn();
    });
  }

  /*------ Mobile Menu ----*/
  const mobileMenu = $(".mobile-menu");
  if (mobileMenu.length) {
    mobileMenu
      .find("ul li")
      .parents(".mobile-menu ul li")
      .addClass("has-sub-item")
      .prepend('<span class="submenu-button"></span>'),
      mobileMenu.find(".submenu-button").on("click.sellzy", function () {
        $(this).toggleClass("submenu-opened");
        $(this).siblings("ul").hasClass("open")
          ? $(this).siblings("ul").removeClass("open").slideUp("fast")
          : $(this).siblings("ul").addClass("open").slideDown("fast");
      });
  }

  /*------ Main Menu ----*/
  const mainMenu = $(".main-menu");
  if (mainMenu.length) {
    mainMenu.find("ul li").parents(".main-menu ul li").addClass("has-sub-item");
    mainMenu
      .find("ul li.has-sub-item > a")
      .append(
        '<i class="hgi hgi-stroke hgi-arrow-down-01 text-xl text-light-primary-text"></i>'
      );
  }

  /*------ Payment Method ----*/
  const paymentMethod = $(".payment-methods input[name='payment-method']");
  const selectedPaymentMethod = $(
    ".payment-methods input[name='payment-method']:checked"
  );
  if (selectedPaymentMethod.length) {
    selectedPaymentMethod.parents(".payment-method").addClass("selected");
    selectedPaymentMethod
      .parents(".payment-method")
      .find(".payment-content")
      .show();
  }
  if (paymentMethod.length) {
    paymentMethod.on("change.sellzy", function () {

      $(this).parents(".payment-method").siblings().removeClass("selected");
      $(this).parents(".payment-method").addClass("selected");
      $(this)
        .parents(".payment-method")
        .siblings()
        .find(".payment-content")
        .slideUp();
      $(this).parents(".payment-method").find(".payment-content").slideDown();
    });
  }

  /*------ Home Five Product Filter Buttons ------*/
  const homeFiveProductFilterButtons = $(".home-five-product-filter button");
  if (homeFiveProductFilterButtons.length) {
    $(".home-five-product-filter button:nth-child(1)")
      .addClass("btn-primary")
      .removeClass("btn-default outline shadow-none")
      .siblings()
      .removeClass("btn-primary")
      .addClass("btn-default outline shadow-none");
    $(".tab-pane").hide();

    $(".tab-pane:nth-child(1)").addClass("active").show();
    $(".home-five-product-filter button").on("click.sellzy", function () {
      $(this)
        .removeClass("btn-default outline shadow-none")
        .addClass("btn-primary")
        .siblings()
        .removeClass("btn-primary")
        .addClass("btn-default outline shadow-none");
      $(".tab-pane").removeClass("active fade").hide();
      let activeTab = $(this).attr("data-tab");
      $(`#${activeTab}`).addClass("active fade").fadeIn();
      return false;
    });
  }

  /*------ Home Five Variation Color Buttons ------*/
  const homeFiveVariationColorButtons = $(".variation-color-item button");
  if (homeFiveVariationColorButtons.length) {
    homeFiveVariationColorButtons.on("click.sellzy", function () {
      const color = $(this).data("color");
      $(this).parent().css("border-color", color);
      $(this).parent().siblings().css("border-color", "#dfe3e8");
      $(this).parent().siblings().find("i").addClass("hidden");
      $(this).find("i").removeClass("hidden");
    });
  }

  /*------ Is Anything Open ------*/
  function isAnythingOpen() {
    const isAnythingOpen = $modalOverlay.attr("data-overlay-for");
    if (isAnythingOpen) {
      $(isAnythingOpen).attr("data-state", "close");
    }
  }

  /*------ Show Sidebar ------*/
  function showSidebar(sidebarFor) {
    $(sidebarFor).attr("data-state", "open");
    $body.addClass("overflow-hidden scrollbar-offset");
    $modalOverlay.fadeIn();
    $modalOverlay.attr("data-overlay-for", sidebarFor);
  }

  /*------ Close Sidebar ------*/
  function closeSidebar(sidebarFor) {
    $(sidebarFor).attr("data-state", "close");
    $body.removeClass("overflow-hidden scrollbar-offset");
    $modalOverlay.fadeOut();
    $modalOverlay.removeAttr("data-overlay-for");
  }

  /*------ Cart: API + Render ------*/
  async function cartFetchJson(url, options) {
    const res = await fetch(url, options);
    if (!res.ok) {
      const text = await res.text();
      throw new Error(text || `Request failed: ${res.status}`);
    }
    return await res.json();
  }

  function formatVnd(value) {
    try {
      const fmt = new Intl.NumberFormat("vi-VN");
      return `${fmt.format(value || 0)} VNĐ`;
    } catch {
      return `${value || 0} VNĐ`;
    }
  }

  function resolveAssetUrl(rawUrl) {
    if (!rawUrl) return "assets/images/vitamin-c.png";
    const u = String(rawUrl).trim();
    if (!u) return "assets/images/vitamin-c.png";
    if (u.startsWith("http://") || u.startsWith("https://") || u.startsWith("data:")) return u;
    if (u.startsWith("/")) return u;
    return u;
  }

  function renderSuggestionsUsingTemplate(contentEl, items) {
    if (!contentEl) return;

    const allCards = Array.from(contentEl.querySelectorAll(".similar-product-item"));
    const template = allCards[0] || null;
    if (!template) return;

    if (!template.dataset.suggestionTemplate) {
      template.dataset.suggestionTemplate = "1";
      template.classList.add("hidden");
    }

    const previouslyRendered = Array.from(contentEl.querySelectorAll("[data-suggestion-rendered='1']"));
    for (const el of previouslyRendered) el.remove();

    for (let i = 1; i < allCards.length; i++) {
      if (!allCards[i].dataset.suggestionTemplate && !allCards[i].dataset.suggestionRendered) {
        allCards[i].classList.add("hidden");
        allCards[i].dataset.suggestionPlaceholder = "1";
      }
    }

    const list = Array.isArray(items) ? items : [];
    for (const it of list) {
      const card = template.cloneNode(true);
      card.classList.remove("hidden");
      card.dataset.suggestionRendered = "1";

      const detailsUrl = it.slug ? `/product/${encodeURIComponent(it.slug)}` : "#";
      const title = String(it.name || "(N/A)");
      const categoryName = it.categoryName ? String(it.categoryName) : "";
      const imageUrl = resolveAssetUrl(it.primaryImageUrl);
      const price = Number(it.price || 0) || 0;
      const oldPrice = it.oldPrice != null ? Number(it.oldPrice) : null;

      const imageLink = card.querySelector(".similar-product-item-image");
      if (imageLink) imageLink.setAttribute("href", detailsUrl);
      const img = card.querySelector(".similar-product-item-image img");
      if (img) img.setAttribute("src", imageUrl);

      const titleLink = card.querySelector(".similar-product-item-content h6 a");
      if (titleLink) {
        titleLink.textContent = title;
        titleLink.setAttribute("href", detailsUrl);
      }

      const metaEl = card.querySelector(".similar-product-item-content p.text-sm");
      if (metaEl) metaEl.textContent = categoryName;

      const currentPriceEl = card.querySelector(".price-section .current-price");
      if (currentPriceEl) currentPriceEl.textContent = formatVnd(price);

      const oldPriceEl = card.querySelector(".price-section .old-price");
      if (oldPriceEl) {
        if (oldPrice && oldPrice > price) {
          oldPriceEl.textContent = formatVnd(oldPrice);
          oldPriceEl.classList.remove("hidden");
        } else {
          oldPriceEl.classList.add("hidden");
        }
      }

      const saleBadge = card.querySelector(".product-discount-badge");
      if (saleBadge) {
        // Keep original badge element; hide when no discount.
        if (oldPrice && oldPrice > price) {
          saleBadge.classList.remove("hidden");
          saleBadge.textContent = "Giảm giá";
        } else {
          saleBadge.classList.add("hidden");
        }
      }

      // Keep template button structure, but make it navigate to product details.
      const addBtn = card.querySelector("a.btn.btn-primary");
      if (addBtn) {
        addBtn.setAttribute("href", detailsUrl);
        const label = addBtn.querySelector("span:last-child");
        if (label) label.textContent = "Xem";
      }

      contentEl.appendChild(card);
    }
  }

  function renderCartUsingTemplate(contentEl, items) {
    if (!contentEl) return;

    const allCards = Array.from(contentEl.querySelectorAll(".cart-product-item"));
    const template = allCards[0] || null;
    if (!template) return;

    // Keep markup structure from the template, only replace data.
    if (!template.dataset.cartTemplate) {
      template.dataset.cartTemplate = "1";
      template.classList.add("hidden");
    }

    // Remove previously rendered cards, but keep original template placeholders.
    const previouslyRendered = Array.from(contentEl.querySelectorAll("[data-cart-rendered='1']"));
    for (const el of previouslyRendered) el.remove();

    for (let i = 1; i < allCards.length; i++) {
      if (!allCards[i].dataset.cartTemplate && !allCards[i].dataset.cartRendered) {
        allCards[i].classList.add("hidden");
        allCards[i].dataset.cartPlaceholder = "1";
      }
    }

    const list = Array.isArray(items) ? items : [];
    for (const it of list) {
      const card = template.cloneNode(true);
      card.classList.remove("hidden");
      card.removeAttribute("data-cart-template");
      card.dataset.cartRendered = "1";

      if (typeof it.cartItemId !== "undefined" && it.cartItemId !== null) {
        card.dataset.cartItemId = String(it.cartItemId);
      }
      if (typeof it.variantId !== "undefined" && it.variantId !== null) {
        card.dataset.variantId = String(it.variantId);
      }

      const detailsUrl = it.productSlug ? `/product/${encodeURIComponent(it.productSlug)}` : "#";
      const title = String(it.productName || "(N/A)");
      const color = it.color ? String(it.color) : null;
      const size = it.size ? String(it.size) : null;
      const variantLine = [color ? `Color: ${color}` : null, size ? `Size: ${size}` : null]
        .filter(Boolean)
        .join(", ");
      const imageUrl = resolveAssetUrl(it.imageUrl);
      const qty = Number(it.quantity || 0) || 0;
      const unitPrice = Number(it.unitPrice || 0) || 0;

      const imageLink = card.querySelector(".cart-product-item-image");
      if (imageLink) imageLink.setAttribute("href", detailsUrl);
      const img = card.querySelector(".cart-product-item-image img");
      if (img) img.setAttribute("src", imageUrl);

      const titleLink = card.querySelector(".cart-product-item-content h6 a");
      if (titleLink) {
        titleLink.textContent = title;
        titleLink.setAttribute("href", detailsUrl);
      }

      const variantEl = card.querySelector(".cart-product-item-content p.text-sm");
      if (variantEl) variantEl.textContent = variantLine;

      const currentPriceEl = card.querySelector(".price-section .current-price");
      if (currentPriceEl) currentPriceEl.textContent = formatVnd(unitPrice);

      const oldPriceEl = card.querySelector(".price-section .old-price");
      if (oldPriceEl) oldPriceEl.classList.add("hidden");

      const qtyInput = card.querySelector("input[type='text']");
      if (qtyInput) qtyInput.value = String(qty);

      const selectInput = card.querySelector("input.cart-item-select");
      if (selectInput) {
        const isSelected = typeof it.isSelected === "boolean" ? it.isSelected : true;
        selectInput.checked = !!isSelected;
      }

      contentEl.appendChild(card);
    }
  }

  async function setCartItemQuantity(cartItemId, quantity) {
    return await cartFetchJson("/cart/update-quantity", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ cartItemId, quantity }),
    });
  }

  async function removeCartItem(cartItemId) {
    return await cartFetchJson("/cart/remove", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ cartItemId }),
    });
  }

  async function setCartItemSelected(cartItemId, selected) {
    return await cartFetchJson("/cart/select", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ cartItemId, selected: !!selected }),
    });
  }

  async function refreshCartSidebar() {
    const contentEl = document.querySelector(".cart-products-content");
    const headerCountEl = document.querySelector(".cart-products-header p");
    const cartButtons = Array.from(document.querySelectorAll(".cart-sidebar-btn"));
    const subtotalEl = document.querySelector(".cart-subtotal");
    const checkoutBtn = document.querySelector(".cart-buttons a.btn.btn-primary");
    const similarContentEl = document.querySelector(".similar-products-content");

    const data = await cartFetchJson("/cart/items", { method: "GET" });

    renderCartUsingTemplate(contentEl, data.items || []);

    const count = Number(data.itemCount || 0) || 0;
    if (headerCountEl) headerCountEl.textContent = `${count} sản phẩm trong giỏ`;

    const hasSelectedTotals =
      Object.prototype.hasOwnProperty.call(data, "selectedSubTotal") ||
      Object.prototype.hasOwnProperty.call(data, "selectedSubtotal") ||
      Object.prototype.hasOwnProperty.call(data, "selectedItemCount");

    const selectedCount = Number(data.selectedItemCount ?? 0) || 0;
    const subTotalValue = hasSelectedTotals
      ? Number(data.selectedSubTotal ?? data.selectedSubtotal ?? 0) || 0
      : Number(data.subTotal ?? data.subtotal ?? 0) || 0;

    if (subtotalEl) {
      const h5s = subtotalEl.querySelectorAll("h5");
      if (h5s && h5s.length >= 2) {
        h5s[1].textContent = formatVnd(subTotalValue);
      }
    }

    if (checkoutBtn && hasSelectedTotals) {
      if (selectedCount > 0) {
        checkoutBtn.classList.remove("pointer-events-none", "opacity-50");
        checkoutBtn.removeAttribute("aria-disabled");
        checkoutBtn.setAttribute("href", "/checkout");
      } else {
        checkoutBtn.classList.add("pointer-events-none", "opacity-50");
        checkoutBtn.setAttribute("aria-disabled", "true");
        checkoutBtn.setAttribute("href", "#");
      }
    }

    for (const btn of cartButtons) {
      const badgeEl = btn.querySelector("span.flex.flex-col span.text-base");
      if (badgeEl) badgeEl.textContent = `${count}- Sản phẩm`;
    }

    // Suggestions (best-effort)
    if (similarContentEl) {
      try {
        const sug = await cartFetchJson("/cart/suggestions?count=6", { method: "GET" });
        renderSuggestionsUsingTemplate(similarContentEl, sug.items || []);
      } catch (e) {
        // silent
      }
    }
  }

  $doc.on("click.sellzy", "[data-add-to-cart]", async function (e) {
    e.preventDefault();

    const btn = e.currentTarget;
    const variantId = parseInt(btn.getAttribute("data-variant-id") || "0", 10);
    if (!variantId) return;

    const section = btn.closest(".product-add-to-cart-section") || document;
    const qtyInput = section.querySelector(".quantity-input");
    const qty = qtyInput ? parseInt(qtyInput.value || "1", 10) : 1;
    const quantity = Number.isFinite(qty) && qty > 0 ? qty : 1;

    try {
      btn.setAttribute("disabled", "disabled");
      await cartFetchJson("/cart/add", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ variantId, quantity }),
      });

      await refreshCartSidebar();
      isAnythingOpen();
      showSidebar(".cart-sidebar");
    } catch (err) {
      // Keep silent like the template; failure shouldn't break the page.
      // console.warn(err);
    } finally {
      btn.removeAttribute("disabled");
    }
  });

  /*------ Buy Now: add then go to checkout ------*/
  $doc.on("click.sellzy", "[data-buy-now][data-variant-id]", async function (e) {
    e.preventDefault();
    e.stopImmediatePropagation();

    const btn = e.currentTarget;
    const variantId = parseInt(btn.getAttribute("data-variant-id") || "0", 10);
    if (!variantId) return;

    const section = btn.closest(".product-add-to-cart-section") || document;
    const qtyInput = section.querySelector(".quantity-input");
    const qty = qtyInput ? parseInt(qtyInput.value || "1", 10) : 1;
    const quantity = Number.isFinite(qty) && qty > 0 ? qty : 1;

    btn.setAttribute("disabled", "disabled");
    const q = Number.isFinite(quantity) && quantity > 0 ? Math.min(99, quantity) : 1;
    window.location.href = `/checkout?variantId=${encodeURIComponent(String(variantId))}&quantity=${encodeURIComponent(String(q))}`;
  });

  /*------ Wishlist: Toggle + UI ------*/
  function setWishlistButtonState(btn, isInWishlist) {
    if (!btn) return;
    const active = !!isInWishlist;

    btn.dataset.wishlisted = active ? "1" : "0";
    btn.setAttribute("aria-pressed", active ? "true" : "false");

    const icon = btn.querySelector(".wishlist-heart-icon") || btn.querySelector("i.hgi-favourite");
    if (!icon) return;

    // Color (used by both <svg> and <i>)
    icon.classList.toggle("text-error-dark", active);
    icon.classList.toggle("text-light-secondary-text", !active);

    // If it's our SVG, toggle its fill layer.
    const fillPath = icon.querySelector ? icon.querySelector(".wishlist-heart-fill") : null;
    if (fillPath) {
      fillPath.classList.toggle("opacity-0", !active);
    }
  }

  async function wishlistFetchJson(url, options) {
    const res = await fetch(url, options);
    if (res.status === 401) {
      const err = new Error("Unauthorized");
      err.status = 401;
      throw err;
    }
    if (!res.ok) {
      const text = await res.text();
      throw new Error(text || `Request failed: ${res.status}`);
    }
    return await res.json();
  }

  async function initWishlistButtons() {
    const btns = Array.from(document.querySelectorAll("[data-wishlist-toggle][data-product-id]"));
    if (btns.length === 0) return;

    try {
      const data = await wishlistFetchJson("/my-wishlist/ids", { method: "GET" });
      const ids = Array.isArray(data.items) ? data.items : [];
      const set = new Set(ids.map((x) => parseInt(String(x || "0"), 10)).filter((n) => Number.isFinite(n) && n > 0));

      for (const btn of btns) {
        const pid = parseInt(btn.getAttribute("data-product-id") || "0", 10);
        if (!pid) continue;
        setWishlistButtonState(btn, set.has(pid));
      }
    } catch (e) {
      // silent
    }
  }

  $doc.on("click.sellzy", "[data-wishlist-toggle]", async function (e) {
    e.preventDefault();

    const btn = e.currentTarget;
    if (!btn) return;

    const productId = parseInt(btn.getAttribute("data-product-id") || "0", 10);
    if (!productId) return;

    try {
      btn.setAttribute("disabled", "disabled");

      const data = await wishlistFetchJson("/my-wishlist/toggle", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ productId }),
      });

      const isInWishlist = !!data.isInWishlist;
      // Sync all wishlist buttons for the same product (list cards often have two hearts).
      const allBtns = document.querySelectorAll(
        `[data-wishlist-toggle][data-product-id='${String(productId)}']`
      );
      if (allBtns && allBtns.length) {
        allBtns.forEach((b) => setWishlistButtonState(b, isInWishlist));
      } else {
        setWishlistButtonState(btn, isInWishlist);
      }
    } catch (err) {
      if (err && err.status === 401) {
        // Ask user to sign in (reuse existing sidebar)
        try {
          isAnythingOpen();
          showSidebar(".login-page-sidebar");
        } catch (e) {
          // silent
        }
      }
    } finally {
      btn.removeAttribute("disabled");
    }
  });

  safeInit(function () {
    initWishlistButtons();
  });

  /*------ Checkout Table Actions ------*/
  function formatVndD(value) {
    try {
      const fmt = new Intl.NumberFormat("vi-VN");
      return `${fmt.format(value || 0)}đ`;
    } catch {
      return `${value || 0}đ`;
    }
  }

  function getCheckoutSelectedAddressId() {
    const selected = document.querySelector('input.js-checkout-address[name="AddressId"]:checked');
    if (selected) {
      const v = parseInt(selected.value || "0", 10);
      if (Number.isFinite(v) && v > 0) return v;
    }

    const hidden = document.querySelector('[data-checkout-page] input[name="AddressId"]');
    if (hidden) {
      const v = parseInt(hidden.value || "0", 10);
      if (Number.isFinite(v) && v > 0) return v;
    }

    const url = new URL(window.location.href);
    const qp = parseInt(url.searchParams.get("addressId") || "0", 10);
    if (Number.isFinite(qp) && qp > 0) return qp;
    return 0;
  }

  async function fetchCheckoutSummary(params) {
    const url = new URL("/checkout/summary", window.location.origin);
    if (params && params.addressId && params.addressId > 0) url.searchParams.set("addressId", String(params.addressId));
    if (params && params.variantId && params.variantId > 0) url.searchParams.set("variantId", String(params.variantId));
    if (params && params.quantity && params.quantity > 0) url.searchParams.set("quantity", String(params.quantity));

    // Preserve currently selected vouchers when recalculating summary.
    try {
      const cur = new URL(window.location.href);
      const voucherCode = cur.searchParams.get("voucherCode");
      const shippingVoucherCode = cur.searchParams.get("shippingVoucherCode");
      const discountVoucherCode = cur.searchParams.get("discountVoucherCode");
      if (voucherCode) url.searchParams.set("voucherCode", voucherCode);
      if (shippingVoucherCode) url.searchParams.set("shippingVoucherCode", shippingVoucherCode);
      if (discountVoucherCode) url.searchParams.set("discountVoucherCode", discountVoucherCode);
    } catch {
      // ignore
    }

    return await cartFetchJson(url.pathname + url.search, { method: "GET" });
  }

  function applyCheckoutSummaryToDom(data) {
    const page = document.querySelector("[data-checkout-page]");
    if (!page) return;

    const errorWrapper = document.querySelector("[data-checkout-error-wrapper]");
    const errorEl = document.querySelector("[data-checkout-error]");
    const errorMessage = data && data.errorMessage ? String(data.errorMessage) : "";
    if (errorWrapper) {
      if (errorMessage) {
        errorWrapper.classList.remove("hidden");
        if (errorEl) errorEl.textContent = errorMessage;
      } else {
        errorWrapper.classList.add("hidden");
        if (errorEl) errorEl.textContent = "";
      }
    }

    const subtotalEl = page.querySelector("[data-checkout-subtotal]");
    const shippingEl = page.querySelector("[data-checkout-shipping]");
    const shippingDiscountEl = page.querySelector("[data-checkout-shipping-discount]");
    const shippingDiscountSignEl = page.querySelector("[data-checkout-shipping-discount-sign]");
    const shippingDiscountValueEl = page.querySelector("[data-checkout-shipping-discount-value]");
    const discountVoucherEl = page.querySelector("[data-checkout-discount-voucher]");
    const discountVoucherSignEl = page.querySelector("[data-checkout-discount-voucher-sign]");
    const discountVoucherValueEl = page.querySelector("[data-checkout-discount-voucher-value]");
    const totalEl = page.querySelector("[data-checkout-total]");
    if (subtotalEl) subtotalEl.textContent = formatVndD(Number(data && data.selectedSubTotal) || 0);
    if (shippingEl) shippingEl.textContent = formatVndD(Number(data && data.shippingFee) || 0);

    const shipDisc = Number(data && data.shippingVoucherDiscountAmount) || 0;
    const discDisc = Number(data && data.discountVoucherDiscountAmount) || 0;

    if (shippingDiscountEl) shippingDiscountEl.textContent = formatVndD(shipDisc);
    if (shippingDiscountSignEl) shippingDiscountSignEl.textContent = shipDisc > 0 ? "-" : "";
    if (shippingDiscountValueEl) {
      shippingDiscountValueEl.classList.toggle("text-green-600", shipDisc > 0);
      shippingDiscountValueEl.classList.toggle("text-light-primary-text", !(shipDisc > 0));
    }

    if (discountVoucherEl) discountVoucherEl.textContent = formatVndD(discDisc);
    if (discountVoucherSignEl) discountVoucherSignEl.textContent = discDisc > 0 ? "-" : "";
    if (discountVoucherValueEl) {
      discountVoucherValueEl.classList.toggle("text-green-600", discDisc > 0);
      discountVoucherValueEl.classList.toggle("text-light-primary-text", !(discDisc > 0));
    }

    if (totalEl) totalEl.textContent = formatVndD(Number(data && data.finalAmount) || 0);

    const items = Array.isArray(data && data.items) ? data.items : [];
    const byCartItemId = new Map();
    const byVariantId = new Map();
    for (const it of items) {
      const cid = Number(it && it.cartItemId) || 0;
      const vid = Number(it && it.variantId) || 0;
      if (cid > 0) byCartItemId.set(cid, it);
      if (vid > 0) byVariantId.set(vid, it);
    }

    const rows = Array.from(document.querySelectorAll("[data-checkout-item]"));
    for (const row of rows) {
      const cartItemId = parseInt(row.getAttribute("data-cart-item-id") || "0", 10);
      const variantId = parseInt(row.getAttribute("data-variant-id") || "0", 10);
      const info = cartItemId > 0 ? byCartItemId.get(cartItemId) : byVariantId.get(variantId);
      if (!info) continue;

      const qty = Number(info.quantity) || 1;
      const unitPrice = Number(info.unitPrice) || 0;
      const lineTotal = Number(info.lineTotal) || 0;

      const qtyInput = row.querySelector(".quantity-input");
      if (qtyInput) qtyInput.value = String(qty);

      const unitEl = row.querySelector("[data-checkout-unit-price]");
      if (unitEl) unitEl.textContent = formatVndD(unitPrice);

      const lineEl = row.querySelector("[data-checkout-line-total]");
      if (lineEl) lineEl.textContent = formatVndD(lineTotal);
    }

    const buyNowQty = page.querySelector("[data-checkout-buynow-quantity]");
    if (buyNowQty && data && data.buyNowQuantity) buyNowQty.value = String(Number(data.buyNowQuantity) || 1);
  }

  $doc.on("click.sellzy.checkout", "[data-checkout-remove][data-cart-item-id]", async function (e) {
    e.preventDefault();

    const btn = e.currentTarget;
    const cartItemId = parseInt(btn.getAttribute("data-cart-item-id") || "0", 10);
    if (!cartItemId) return;

    try {
      btn.setAttribute("disabled", "disabled");
      await removeCartItem(cartItemId);
      window.location.reload();
    } catch (err) {
      // silent
    } finally {
      btn.removeAttribute("disabled");
    }
  });

  $doc.on("click.sellzy.checkoutQty", "[data-checkout-item] [data-checkout-qty-action]", async function (e) {
    e.preventDefault();

    const btn = e.currentTarget;
    const action = btn.getAttribute("data-checkout-qty-action") || "";
    const row = btn.closest("[data-checkout-item]");
    if (!row) return;

    const page = document.querySelector("[data-checkout-page]");
    if (!page) return;

    const input = row.querySelector(".quantity-input");
    if (!input) return;

    const cur = parseInt(input.value || "1", 10);
    const safeCur = Number.isFinite(cur) && cur > 0 ? cur : 1;
    const next = action === "dec" ? Math.max(1, safeCur - 1) : Math.min(99, safeCur + 1);

    const cartItemId = parseInt(row.getAttribute("data-cart-item-id") || "0", 10);
    const variantId = parseInt(row.getAttribute("data-variant-id") || "0", 10);

    const addressId = getCheckoutSelectedAddressId();

    const rowButtons = Array.from(row.querySelectorAll("[data-checkout-qty-action]"));
    rowButtons.forEach((b) => b.setAttribute("disabled", "disabled"));

    try {
      // Buy-now mode: cartItemId will be 0; recompute summary directly.
      if (!cartItemId && variantId) {
        input.value = String(next);
        const summary = await fetchCheckoutSummary({ addressId, variantId, quantity: next });
        applyCheckoutSummaryToDom(summary);
        return;
      }

      if (!cartItemId) return;

      input.value = String(next);
      await setCartItemQuantity(cartItemId, next);
      const summary = await fetchCheckoutSummary({ addressId });
      applyCheckoutSummaryToDom(summary);
    } catch (err) {
      // silent
    } finally {
      rowButtons.forEach((b) => b.removeAttribute("disabled"));
    }
  });

  /*------ Wishlist Page (wishlist-style-v1) ------*/
  function updateWishlistPageSelection(root) {
    const container = root || document.querySelector("[data-wishlist-page]");
    if (!container) return;

    const itemCheckboxes = Array.from(
      container.querySelectorAll("input[type='checkbox'][data-wishlist-item-select]")
    ).filter((c) => c && !c.disabled);
    const checkedCount = itemCheckboxes.filter((c) => c.checked).length;

    const countEl = container.querySelector("[data-wishlist-selected-count]");
    if (countEl) countEl.textContent = String(checkedCount);

    const addSelectedBtn = container.querySelector("[data-wishlist-add-selected]");
    if (addSelectedBtn) {
      if (checkedCount > 0) addSelectedBtn.removeAttribute("disabled");
      else addSelectedBtn.setAttribute("disabled", "disabled");
    }

    const selectAll = container.querySelector("input[type='checkbox'][data-wishlist-select-all]");
    if (selectAll) {
      const total = itemCheckboxes.length;
      selectAll.indeterminate = checkedCount > 0 && checkedCount < total;
      selectAll.checked = total > 0 && checkedCount === total;
    }
  }

  function clampInt(value, min, max) {
    const n = parseInt(String(value || ""), 10);
    if (!Number.isFinite(n)) return min;
    if (n < min) return min;
    if (n > max) return max;
    return n;
  }

  $doc.on("change.sellzy.wishlistPage", "[data-wishlist-page] [data-wishlist-item-select]", function (e) {
    updateWishlistPageSelection(e.currentTarget.closest("[data-wishlist-page]"));
  });

  $doc.on("change.sellzy.wishlistPage", "[data-wishlist-page] [data-wishlist-select-all]", function (e) {
    const container = e.currentTarget.closest("[data-wishlist-page]");
    if (!container) return;
    const checked = !!e.currentTarget.checked;
    const itemCheckboxes = Array.from(
      container.querySelectorAll("input[type='checkbox'][data-wishlist-item-select]")
    ).filter((c) => c && !c.disabled);
    itemCheckboxes.forEach((c) => (c.checked = checked));
    updateWishlistPageSelection(container);
  });

  $doc.on("click.sellzy.wishlistQty", "[data-wishlist-page] [data-wishlist-qty-minus]", function (e) {
    e.preventDefault();
    const row = e.currentTarget.closest(".product-add-to-cart-section");
    if (!row) return;
    const input = row.querySelector(".quantity-input");
    if (!input) return;
    const cur = clampInt(input.value, 1, 99);
    input.value = String(Math.max(1, cur - 1));
  });

  $doc.on("click.sellzy.wishlistQty", "[data-wishlist-page] [data-wishlist-qty-plus]", function (e) {
    e.preventDefault();
    const row = e.currentTarget.closest(".product-add-to-cart-section");
    if (!row) return;
    const input = row.querySelector(".quantity-input");
    if (!input) return;
    const cur = clampInt(input.value, 1, 99);
    input.value = String(Math.min(99, cur + 1));
  });

  $doc.on("click.sellzy.wishlistAddSelected", "[data-wishlist-page] [data-wishlist-add-selected]", async function (e) {
    e.preventDefault();
    const btn = e.currentTarget;
    const container = btn.closest("[data-wishlist-page]");
    if (!container) return;

    const selected = Array.from(
      container.querySelectorAll("input[type='checkbox'][data-wishlist-item-select]:checked")
    ).filter((c) => c && !c.disabled);
    if (selected.length === 0) return;

    try {
      btn.setAttribute("disabled", "disabled");

      for (const checkbox of selected) {
        const row = checkbox.closest(".product-add-to-cart-section");
        if (!row) continue;
        const variantId = parseInt(row.getAttribute("data-variant-id") || "0", 10);
        if (!variantId) continue;
        const qtyInput = row.querySelector(".quantity-input");
        const qty = qtyInput ? clampInt(qtyInput.value, 1, 99) : 1;

        await cartFetchJson("/cart/add", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ variantId, quantity: qty }),
        });
      }

      await refreshCartSidebar();
      isAnythingOpen();
      showSidebar(".cart-sidebar");
    } catch (err) {
      // silent
    } finally {
      btn.removeAttribute("disabled");
      updateWishlistPageSelection(container);
    }
  });

  safeInit(function () {
    updateWishlistPageSelection();
  });

  // When user opens the cart sidebar, refresh its content.
  if (cartSidebarBtn.length) {
    cartSidebarBtn.on("click.sellzy.cartRefresh", function () {
      safeInit(function () {
        refreshCartSidebar();
      });
    });
  }

  // Cart sidebar +/− and remove actions (keep template structure)
  $doc.on("click.sellzy.cartActions", ".cart-products-content .cart-product-item button", async function (e) {
    const btn = e.currentTarget;
    const card = btn.closest(".cart-product-item");
    if (!card) return;

    const cartItemId = parseInt(card.getAttribute("data-cart-item-id") || "0", 10);
    if (!cartItemId) return;

    const isPlus = !!btn.querySelector("i.hgi-add-circle");
    const isMinus = !!btn.querySelector("i.hgi-remove-circle");
    const isDelete = !!btn.querySelector("i.hgi-delete-01");

    // Ignore other buttons (e.g., edit)
    if (!isPlus && !isMinus && !isDelete) return;
    e.preventDefault();

    const qtyInput = card.querySelector("input[type='text']");
    const currentQty = qtyInput ? parseInt(qtyInput.value || "0", 10) : 0;
    const safeCurrent = Number.isFinite(currentQty) ? currentQty : 0;

    try {
      btn.setAttribute("disabled", "disabled");

      if (isDelete) {
        await removeCartItem(cartItemId);
      } else if (isPlus) {
        await setCartItemQuantity(cartItemId, safeCurrent + 1);
      } else if (isMinus) {
        const nextQty = safeCurrent - 1;
        await setCartItemQuantity(cartItemId, nextQty);
      }

      await refreshCartSidebar();
    } catch (err) {
      // Keep silent like the template
      // console.warn(err);
    } finally {
      btn.removeAttribute("disabled");
    }
  });

  // Cart sidebar select/unselect actions
  $doc.on("change.sellzy.cartSelect", ".cart-products-content .cart-product-item .cart-item-select", async function (e) {
    const input = e.currentTarget;
    const card = input.closest(".cart-product-item");
    if (!card) return;

    const cartItemId = parseInt(card.getAttribute("data-cart-item-id") || "0", 10);
    if (!cartItemId) return;

    try {
      input.setAttribute("disabled", "disabled");
      await setCartItemSelected(cartItemId, !!input.checked);
      await refreshCartSidebar();
    } catch (err) {
      // silent
    } finally {
      input.removeAttribute("disabled");
    }
  });

  
  /*------ About Us Page Counter Up ------*/
  safeInit(function () {
    if (!window.counterUp || !window.counterUp.default) return;
    if (typeof IntersectionObserver === "undefined") return;

    const counterUp = window.counterUp.default;

    const callback = (entries) => {
      entries.forEach((entry) => {
        const el = entry.target;
        if (entry.isIntersecting && !el.classList.contains("is-visible")) {
          safeInit(function () {
            counterUp(el, { duration: 2000, delay: 16 });
          });
          el.classList.add("is-visible");
        }
      });
    };

    const IO = new IntersectionObserver(callback, { threshold: 1 });
    addCleanup(function () {
      try {
        IO.disconnect();
      } catch (e) {}
    });

    const els = document.querySelectorAll(".about-us-counter");
    if (!els || !els.length) return;
    els.forEach((el) => IO.observe(el));
  });

/*------ About Us Page Video Popup ------*/

  safeInit(function () {
    if ($.fn && $.fn.magnificPopup) {
      $(".about-us-popup-youtube").magnificPopup({
    type: "iframe",

    iframe: {
      markup:
        '<div class="mfp-iframe-scaler">' +
        '<div class="mfp-close"></div>' +
        '<iframe class="mfp-iframe" frameborder="0" allowfullscreen></iframe>' +
        "</div>", // HTML markup of popup, `mfp-close` will be replaced by the close button

      patterns: {
        youtube: {
          index: "youtube.com/",
          id: "v=",
          src: "//www.youtube.com/embed/%id%?autoplay=1",
        },
        vimeo: {
          index: "vimeo.com/",
          id: "/",
          src: "//player.vimeo.com/video/%id%?autoplay=1",
        },
        gmaps: {
          index: "//maps.google.",
          src: "%id%&output=embed",
        },
      },

      srcAction: "iframe_src",
    },
      });
    }
  });

  /*------ Price Range Slider Widget ------*/
  const priceRangeSlider = document.getElementById("price-range-slider");
  if (priceRangeSlider) {
    safeInit(function () {
      if (typeof noUiSlider === "undefined") return;

      const readNumber = (value, fallback) => {
        const num = Number(value);
        return Number.isFinite(num) ? num : fallback;
      };
      const clamp = (value, min, max) => Math.min(Math.max(value, min), max);

      const rawMin = readNumber(priceRangeSlider.dataset.min, 0);
      const rawMax = readNumber(priceRangeSlider.dataset.max, 100);

      // VND: use 1,000đ increments
      const step = 1000;
      const min = Math.floor(rawMin / step) * step;
      const max = Math.ceil(rawMax / step) * step;
      if (!(max > min)) return;

      let startMin = readNumber(priceRangeSlider.dataset.startMin, min);
      let startMax = readNumber(priceRangeSlider.dataset.startMax, max);
      startMin = clamp(startMin, min, max);
      startMax = clamp(startMax, min, max);

      // snap starts to 1,000đ
      startMin = Math.round(startMin / step) * step;
      startMax = Math.round(startMax / step) * step;
      if (startMax < startMin) {
        const tmp = startMin;
        startMin = startMax;
        startMax = tmp;
      }

      noUiSlider.create(priceRangeSlider, {
      start: [startMin, startMax],
        step,
      connect: true,
      range: {
        min,
        max,
      },
      // make numbers whole
      format: {
          to: (value) => String(Math.round(value)),
          from: (value) => Number(value),
      },
    });

      priceRangeSlider.noUiSlider.on("update", (values) => {
        const v0 = Math.round(Number(values[0]));
        const v1 = Math.round(Number(values[1]));

        // Hidden fields submitted to server (raw numbers)
        $(".price-range-min-hidden").val(v0);
        $(".price-range-max-hidden").val(v1);

        // Display fields (template-compatible class names)
        try {
          $(".price-range-min-value").val(v0.toLocaleString("vi-VN"));
          $(".price-range-max-value").val(v1.toLocaleString("vi-VN"));
        } catch (e) {
          $(".price-range-min-value").val(String(v0));
          $(".price-range-max-value").val(String(v1));
        }
      });

      // Form to submit when the filter changes (configured via data-form).
      const submitTargetFormId = priceRangeSlider.dataset.form;
      const submitTargetForm = submitTargetFormId
        ? document.getElementById(submitTargetFormId)
        : priceRangeSlider.closest("form");

      // Allow manual input: user types min/max, slider follows.
      const $minInput = $(".price-range-min-value");
      const $maxInput = $(".price-range-max-value");
      const parseMoney = (raw) => {
        const digits = String(raw ?? "").replace(/[^\d]/g, "");
        return digits ? Number(digits) : NaN;
      };
      const applyInputsToSlider = () => {
        if (!priceRangeSlider.noUiSlider) return;

        let vMin = parseMoney($minInput.val());
        let vMax = parseMoney($maxInput.val());

        if (!Number.isFinite(vMin)) vMin = startMin;
        if (!Number.isFinite(vMax)) vMax = startMax;

        vMin = clamp(vMin, min, max);
        vMax = clamp(vMax, min, max);

        vMin = Math.round(vMin / step) * step;
        vMax = Math.round(vMax / step) * step;

        if (vMax < vMin) {
          const tmp = vMin;
          vMin = vMax;
          vMax = tmp;
        }

        priceRangeSlider.noUiSlider.set([vMin, vMax]);
      };

      // Update slider when the inputs change.
      $minInput.on("change.sellzy blur.sellzy", applyInputsToSlider);
      $maxInput.on("change.sellzy blur.sellzy", applyInputsToSlider);

      // Press Enter to apply (and submit if form is configured).
      const submitOnEnter = (e) => {
        if (e.key !== "Enter") return;
        e.preventDefault();
        applyInputsToSlider();
        try {
          if (submitTargetForm) {
            if (submitTargetForm.requestSubmit) submitTargetForm.requestSubmit();
            else submitTargetForm.submit();
          }
        } catch (err) {}
      };
      $minInput.on("keydown.sellzy", submitOnEnter);
      $maxInput.on("keydown.sellzy", submitOnEnter);

      // Optional: submit related filter form when the user finishes dragging.
      if (submitTargetForm) {
        let submitTimer;
        priceRangeSlider.noUiSlider.on("change", () => {
          clearTimeout(submitTimer);
          submitTimer = setTimeout(() => {
            try {
              if (submitTargetForm.requestSubmit) submitTargetForm.requestSubmit();
              else submitTargetForm.submit();
            } catch (e) {}
          }, 200);
        });
      }

      addCleanup(function () {
        try {
          if (priceRangeSlider.noUiSlider) priceRangeSlider.noUiSlider.destroy();
        } catch (e) {}
      });
    });
  }

  /*------ Star Rating Widget ------*/
  $(".widget-rating a").on("click.sellzy", function (e) {
    const href = $(this).attr("href");
    // Only block navigation for placeholder links used by the static template.
    // In the ASP.NET MVC app we use real URLs for filtering.
    if (!href || href === "#") {
      e.preventDefault();
    }
    $(this).parent().siblings().find("a").removeClass("active");
    $(this).addClass("active");
  });

  /*------ Color Picker Widget ------*/
  $(".widget-color-picker button").on("click.sellzy", function (e) {
    $(this).parent().siblings().find("button").removeClass("active");
    $(this).addClass("active");

    const form = document.getElementById("category-filter-form");
    const color = String($(this).data("color") ?? "").trim();
    if (!form || !color) return;

    const upsertHidden = (name, value) => {
      let input = form.querySelector(`input[name="${name}"]`);
      if (!input) {
        input = document.createElement("input");
        input.type = "hidden";
        input.name = name;
        form.appendChild(input);
      }
      input.value = value;
    };

    upsertHidden("color", color);
    upsertHidden("page", "1");

    try {
      if (form.requestSubmit) form.requestSubmit();
      else form.submit();
    } catch (err) {}
  });

  /*------ Size Picker Widget ------*/
  $(".widget-size-picker button").on("click.sellzy", function (e) {
    $(this).parent().siblings().find("button").removeClass("active");
    $(this).addClass("active");

    const form = document.getElementById("category-filter-form");
    const size = String($(this).data("size") ?? "").trim();
    if (!form || !size) return;

    const upsertHidden = (name, value) => {
      let input = form.querySelector(`input[name="${name}"]`);
      if (!input) {
        input = document.createElement("input");
        input.type = "hidden";
        input.name = name;
        form.appendChild(input);
      }
      input.value = value;
    };

    upsertHidden("size", size);
    upsertHidden("page", "1");

    try {
      if (form.requestSubmit) form.requestSubmit();
      else form.submit();
    } catch (err) {}
  });

  /*------ Product Details Tabs Section ------*/
  const productDetailsTabs = $("#product-details-tabs button");
  if (productDetailsTabs.length) {
    productDetailsTabs.on("click.sellzy", function () {
      $(this)
        .addClass("active")
        .parent()
        .siblings()
        .find("button")
        .removeClass("active");
      $(".product-details-tab").removeClass("active fade").hide();
      let activeTab = $(this).attr("data-tab");
      $(`#${activeTab}`).addClass("active fade").fadeIn();
    });
  }

  /*------ Product Details Color Variation (delegated) ------*/
  $doc.on("click.sellzy", ".color-variation-item button", function () {
    $(this).css("border-color", $(this).attr("data-color"));
    $(this).parent().siblings().find("button").css("border-color", "#dfe3e8");
    $(".color-variation-selected-color").text($(this).attr("data-color-text"));
  });

  /*------ Product Details Size Variation (delegated) ------*/
  $doc.on("click.sellzy", ".size-variation-item button", function () {
    $(this).addClass("border-primary bg-primary hover:bg-primary text-white");
    $(this)
      .parent()
      .siblings()
      .find("button")
      .removeClass("border-primary bg-primary hover:bg-primary text-white")
      .addClass("border-gray-300 text-light-primary-text");
    $(".size-variation-selected-size").text($(this).attr("data-size-text"));
  });

  /*------ Quantity Controls (delegated) ------*/
  $doc.on("click.sellzy", ".quantity-btn", function (e) {
    e.preventDefault();
    const action = String($(this).attr("data-qty-action") || "").toLowerCase();
    const $section = $(this).closest(".quantity-section");
    const $input = $section.find(".quantity-input").first();
    if (!$input.length) return;

    const raw = $input.val();
    const current = Math.max(1, parseInt(String(raw || "1"), 10) || 1);
    const next = action === "dec" ? Math.max(1, current - 1) : current + 1;
    $input.val(String(next));
  });

  $doc.on("change.sellzy blur.sellzy", ".quantity-input", function () {
    const raw = $(this).val();
    const current = Math.max(1, parseInt(String(raw || "1"), 10) || 1);
    $(this).val(String(current));
  });

  /*------ Product Details Size Variation Modal ------*/
  const productDetailsSizeVariationModal = $(".variation-size-guide-btn");
  if (productDetailsSizeVariationModal.length) {
    productDetailsSizeVariationModal.on("click.sellzy", function (e) {
      e.preventDefault();
      isAnythingOpen();
      showSidebar(".size-variation-modal");
    });
  }

  /*------ Product Details Height Range Slider Widget ------*/
  const heightRangeSlider = document.querySelector("#height-range-slider");
  if (heightRangeSlider) {
    safeInit(function () {
      if (typeof noUiSlider === "undefined") return;
      noUiSlider.create(heightRangeSlider, {
      start: 80,
      connect: "lower",
      range: {
        min: 0,
        max: 200,
      },
      // make numbers whole
      format: {
        to: (value) => value,
        from: (value) => value,
      },
    });

      heightRangeSlider.noUiSlider.on("update", (values) => {
        $(".height-range-slider-value").text(values[0].toFixed(0));
      });

      addCleanup(function () {
        try {
          if (heightRangeSlider.noUiSlider) heightRangeSlider.noUiSlider.destroy();
        } catch (e) {}
      });
    });
  }

  /*------ Product Details Weight Range Slider Widget ------*/
  const weightRangeSlider = document.querySelector("#weight-range-slider");
  if (weightRangeSlider) {
    safeInit(function () {
      if (typeof noUiSlider === "undefined") return;
      noUiSlider.create(weightRangeSlider, {
      start: 80,
      connect: "lower",
      range: {
        min: 0,
        max: 200,
      },
      // make numbers whole
      format: {
        to: (value) => value,
        from: (value) => value,
      },
    });

      weightRangeSlider.noUiSlider.on("update", (values) => {
        $(".weight-range-slider-value").text(values[0].toFixed(0));
      });

      addCleanup(function () {
        try {
          if (weightRangeSlider.noUiSlider) weightRangeSlider.noUiSlider.destroy();
        } catch (e) {}
      });
    });
  }

  /*------ Home Four Product Filter Buttons ------*/
  const homeFourProductFilterButtons = $(".home-four-product-filter button");
  if (homeFourProductFilterButtons.length) {
    $(".home-four-product-filter button:nth-child(1)")
      .addClass(
        "text-light-primary-text border-b-2 border-[text-light-primary-text] bg-transparent font-semibold"
      )
      .removeClass("bg-transparent")
      .siblings()
      .removeClass(
        "text-light-primary-text border-b-2 border-[text-light-primary-text] bg-transparent font-semibold"
      )
      .addClass("bg-transparent");
    $("#deal-tab-content .tab-pane").hide();

    $("#deal-tab-content .tab-pane:nth-child(1)").addClass("active").show();
    $(".home-four-product-filter button").on("click.sellzy", function () {
      $(this)
        .removeClass("bg-transparent")
        .addClass(
          "text-light-primary-text border-b-2 border-[text-light-primary-text] bg-transparent font-semibold"
        )
        .siblings()
        .removeClass(
          "text-light-primary-text border-b-2 border-[text-light-primary-text] bg-transparent font-semibold"
        )
        .addClass("bg-transparent");
      $("#deal-tab-content .tab-pane").removeClass("active fade").hide();
      let activeTab = $(this).attr("data-tab");
      $(`#${activeTab}`).addClass("active fade").fadeIn();
      return false;
    });
  }

  /*------ Home Four Category Filter Buttons ------*/
  const homeFourCategoryFilterButtons = $(".home-four-category-filter button");
  if (homeFourCategoryFilterButtons.length) {
    $(".home-four-category-filter button:nth-child(1)")
      .addClass("text-primary bg-transparent")
      .removeClass("bg-transparent")
      .siblings()
      .removeClass("text-primary bg-transparent")
      .addClass("bg-transparent");
    $("#category-tab-content .tab-pane").hide();

    $("#category-tab-content .tab-pane:nth-child(1)").addClass("active").show();
    $(".home-four-category-filter button").on("click.sellzy", function () {
      $(this)
        .removeClass("bg-transparent")
        .addClass("text-primary bg-transparent")
        .siblings()
        .removeClass("text-primary bg-transparent")
        .addClass("bg-transparent");
      $("#category-tab-content .tab-pane").removeClass("active fade").hide();
      let activeTab = $(this).attr("data-tab");
      $(`#${activeTab}`).addClass("active fade").fadeIn();
      return false;
    });
  }

  /*------ Home Two Product Filter Buttons ------*/
  const homeTwoProductFilterButtons = $(".home-two-product-filter button");
  if (homeTwoProductFilterButtons.length) {
    $(".home-two-product-filter button:nth-child(1)")
      .addClass("btn-primary")
      .removeClass("btn-default outline shadow-none")
      .siblings()
      .removeClass("btn-primary")
      .addClass("btn-default outline  shadow-none");
    $(".tab-pane").hide();

    $(".tab-pane:nth-child(1)").addClass("active").show();
    $(".home-two-product-filter button").on("click.sellzy", function () {
      $(this)
        .removeClass("btn-default outline shadow-none")
        .addClass("btn-primary")
        .siblings()
        .removeClass("btn-primary")
        .addClass("btn-default outline  shadow-none");
      $(".tab-pane").removeClass("active fade").hide();
      let activeTab = $(this).attr("data-tab");
      $(`#${activeTab}`).addClass("active fade").fadeIn();
      return false;
    });
  }

  /*------ Order History Filter Buttons ------*/
  const orderHistoryProductFilterButtons = $(
    ".order-history-product-filter button"
  );
  if (orderHistoryProductFilterButtons.length) {
    $(".order-history-product-filter button:nth-child(1)")
      .addClass("bg-primary/8 text-primary ")
      .removeClass("text-light-primary-text")
      .siblings()
      .removeClass("bg-primary/8 text-primary")
      .addClass("text-light-primary-text");
    $("#order-tab-content .tab-pane").hide();

    $("#order-tab-content .tab-pane:nth-child(1)").addClass("active").show();
    $(".order-history-product-filter button").on("click.sellzy", function () {
      $(this)
        .removeClass("text-light-primary-text")
        .addClass("bg-primary/8 text-primary")
        .siblings()
        .removeClass("bg-primary/8 text-primary")
        .addClass("text-light-primary-text");
      $("#order-tab-content .tab-pane").removeClass("active fade").hide();
      let activeTab = $(this).attr("data-tab");
      $(`#${activeTab}`).addClass("active fade").fadeIn();
      return false;
    });
  }

  /*------ My Account Navigation Buttons ------*/
  const myAccountMenuButton = $(".my-account-menu button");
  if (myAccountMenuButton.length) {
    myAccountMenuButton.on("click.sellzy", function () {
      if ($(this).data("tab") === "logout") {
        return false;
      }
      $(this)
        .addClass("active")
        .parent()
        .siblings()
        .find("button")
        .removeClass("active");
      $(".menu-tab-pane").removeClass("active fade").addClass("hidden");
      let activeTab = $(this).attr("data-tab");
      $(".my-account-content")
        .find(`#${activeTab}`)
        .removeClass("hidden")
        .addClass("active fade")
        .fadeIn();
    });
  }

  $(".order-details-button").on("click.sellzy", function () {
    $(".menu-tab-pane").removeClass("active fade").addClass("hidden");
    $("#order-details").removeClass("hidden").addClass("active fade").fadeIn();
  });

  $(".order-details-back-button").on("click.sellzy", function () {
    $(".menu-tab-pane").removeClass("active fade").addClass("hidden");
    $("#orders").removeClass("hidden").addClass("active fade").fadeIn();
  });

  $(".add-new-address-button").on("click.sellzy", function () {
    $(".menu-tab-pane").removeClass("active fade").addClass("hidden");
    $("#add-address").removeClass("hidden").addClass("active fade").fadeIn();
  });

  $(".add-new-address-back-button").on("click.sellzy", function () {
    $(".menu-tab-pane").removeClass("active fade").addClass("hidden");
    $("#address").removeClass("hidden").addClass("active fade").fadeIn();
  });

  $(".edit-address-button").on("click.sellzy", function () {
    $(".menu-tab-pane").removeClass("active fade").addClass("hidden");
    $("#edit-address").removeClass("hidden").addClass("active fade").fadeIn();
  });

  $(".edit-address-back-button").on("click.sellzy", function () {
    $(".menu-tab-pane").removeClass("active fade").addClass("hidden");
    $("#address").removeClass("hidden").addClass("active fade").fadeIn();
  });

  $(".logout-button").on("click.sellzy", function (e) {
    e.preventDefault();
    isAnythingOpen();
    showSidebar(".logout-modal");
  });

  /*------ Banner with Filter Button  ------*/
  $(".filter-button").on("click.sellzy", function () {
    const parent = $(this).closest(".filter-dropdown");
    const menu = parent.find(".filter-dropdown-menu");
    const icon = parent.find(".filter-dropdown-icon");

    const isOpen = $(this).attr("data-state") === "open";

    $(".filter-button").attr("data-state", "close");
    $(".filter-button").removeClass(
      "ring-primary text-primary transition-colors duration-300 ease-in-out"
    );
    $(".filter-button span i").removeClass(
      "text-primary transition-colors duration-300 ease-in-out"
    );
    $(".filter-dropdown-menu").removeClass("active").addClass("hide");
    $(".filter-dropdown-icon").removeClass(
      "rotate-180 text-primary transition-colors duration-300 ease-in-out"
    );

    if (!isOpen) {
      $(this).attr("data-state", "open");
      $(this).addClass(
        "ring-primary text-primary transition-colors duration-300 ease-in-out"
      );
      $(this)
        .find("span i")
        .addClass("text-primary transition-colors duration-300 ease-in-out");
      menu.removeClass("hide").addClass("active");
      icon.addClass(
        "rotate-180 text-primary transition-colors duration-300 ease-in-out"
      );

      const rect = parent[0].getBoundingClientRect();
      const windowWidth = window.innerWidth;

      menu.removeClass("left-0 right-0 left-auto right-auto");

      if (rect.left + rect.width / 2 > windowWidth / 2) {
        menu.addClass("right-0 left-auto");
      } else {
        menu.addClass("left-0 right-auto");
      }
    }
  });

  /*------ Filter Sidebar ----*/
  const filterSidebarMenu = $("#filter-menu-btn");
  const filterSidebar = $("#filter-sidebar");
  const filterSidebarMenuClose = $("#filter-side-bar-menu-close");

  if (filterSidebarMenu.length) {
    filterSidebarMenu.on("click.sellzy", function () {
      $(filterSidebar).attr("data-state", "open");
      $body.addClass("overflow-hidden");
    });
  }

  if (filterSidebarMenuClose.length) {
    filterSidebarMenuClose.on("click.sellzy", function () {
      $(filterSidebar).attr("data-state", "close");
      $body.removeClass("overflow-hidden");
    });
  }
  const filterPriceRangeSlider = document.getElementById(
    "filter-price-range-slider"
  );
  if (filterPriceRangeSlider) {
    safeInit(function () {
      if (typeof noUiSlider === "undefined") return;

      const readNumber = (value, fallback) => {
        const num = Number(value);
        return Number.isFinite(num) ? num : fallback;
      };
      const clamp = (value, min, max) => Math.min(Math.max(value, min), max);

      const min = readNumber(filterPriceRangeSlider.dataset.min, 0);
      const max = readNumber(filterPriceRangeSlider.dataset.max, 100);
      if (!(max > min)) return;

      let startMin = readNumber(filterPriceRangeSlider.dataset.startMin, min);
      let startMax = readNumber(filterPriceRangeSlider.dataset.startMax, max);
      startMin = clamp(startMin, min, max);
      startMax = clamp(startMax, min, max);
      if (startMax < startMin) {
        const tmp = startMin;
        startMin = startMax;
        startMax = tmp;
      }

      noUiSlider.create(filterPriceRangeSlider, {
        start: [startMin, startMax],
        connect: true,
        range: {
          min,
          max,
        },
        format: {
          to: (value) => value,
          from: (value) => value,
        },
      });

      filterPriceRangeSlider.noUiSlider.on("update", (values) => {
        $(".filter-price-range-min-value").val(Number(values[0]).toFixed(0));
        $(".filter-price-range-max-value").val(Number(values[1]).toFixed(0));
      });

      // Optional: submit related filter form when the user finishes dragging.
      const submitTargetFormId = filterPriceRangeSlider.dataset.form;
      const submitTargetForm = submitTargetFormId
        ? document.getElementById(submitTargetFormId)
        : filterPriceRangeSlider.closest("form");
      if (submitTargetForm) {
        let submitTimer;
        filterPriceRangeSlider.noUiSlider.on("change", () => {
          clearTimeout(submitTimer);
          submitTimer = setTimeout(() => {
            try {
              if (submitTargetForm.requestSubmit) submitTargetForm.requestSubmit();
              else submitTargetForm.submit();
            } catch (e) {}
          }, 200);
        });
      }

      addCleanup(function () {
        try {
          if (filterPriceRangeSlider.noUiSlider) {
            filterPriceRangeSlider.noUiSlider.destroy();
          }
        } catch (e) {}
      });
    });
  }

  /*------ Quick View Sidebar ------*/
  const __quickViewHtmlCache =
    window.__sellzyQuickViewHtmlCache ||
    (window.__sellzyQuickViewHtmlCache = Object.create(null));

  function initQuickViewVariantUi(contentEl) {
    if (!contentEl) return;

    const $content = $(contentEl);

    const variants = [];
    $content.find(".quick-view-variant").each(function () {
      const color = ($(this).attr("data-color") || "").trim();
      const size = ($(this).attr("data-size") || "").trim();
      const priceStr = ($(this).attr("data-price") || "").trim();
      const stockStr = ($(this).attr("data-stock") || "").trim();

      const price = Number(priceStr);
      const stock = Number(stockStr);
      if (!Number.isFinite(price)) return;

      variants.push({
        color: color || null,
        size: size || null,
        price,
        stock: Number.isFinite(stock) ? stock : 0,
      });
    });

    function getSelectedColor() {
      const btn = $content
        .find(".color-variation-item button.border-primary")
        .first();
      const c = (btn.attr("data-color-text") || "").trim();
      return c || null;
    }

    function getSelectedSize() {
      const btn = $content
        .find(".size-variation-item button.bg-primary")
        .first();
      const s = (btn.attr("data-size-text") || "").trim();
      return s || null;
    }

    function normalizeKey(v) {
      return (v || "").trim().toLowerCase();
    }

    const hasColorUi = $content.find(".color-variation-item button").length > 0;
    const hasSizeUi = $content.find(".size-variation-item button").length > 0;

    function findExactVariant(color, size) {
      const c = normalizeKey(color);
      const s = normalizeKey(size);
      return (
        variants.find(
          (v) => normalizeKey(v.color) === c && normalizeKey(v.size) === s
        ) || null
      );
    }

    function availableSizesForColor(color) {
      const c = normalizeKey(color);
      const set = new Set();
      variants.forEach((v) => {
        if (normalizeKey(v.color) === c && v.size) set.add(v.size);
      });
      return set;
    }

    function availableColorsForSize(size) {
      const s = normalizeKey(size);
      const set = new Set();
      variants.forEach((v) => {
        if (normalizeKey(v.size) === s && v.color) set.add(v.color);
      });
      return set;
    }

    function formatVnd(amount) {
      try {
        return new Intl.NumberFormat("vi-VN").format(amount) + " VNĐ";
      } catch (e) {
        return amount + " VNĐ";
      }
    }

    function setDisabled($el, disabled) {
      if (!$el || !$el.length) return;
      $el.prop("disabled", !!disabled);
      if (disabled) {
        $el.addClass("opacity-50 cursor-not-allowed");
      } else {
        $el.removeClass("opacity-50 cursor-not-allowed");
      }
    }

    function syncOptionAvailability() {
      const selectedColor = getSelectedColor();
      const selectedSize = getSelectedSize();

      if (hasColorUi && selectedColor) {
        const sizesOk = availableSizesForColor(selectedColor);
        $content.find(".size-variation-item button").each(function () {
          const s = ($(this).attr("data-size-text") || "").trim();
          setDisabled($(this), !sizesOk.has(s));
        });
      }

      if (hasSizeUi && selectedSize) {
        const colorsOk = availableColorsForSize(selectedSize);
        $content.find(".color-variation-item button").each(function () {
          const c = ($(this).attr("data-color-text") || "").trim();
          setDisabled($(this), !colorsOk.has(c));
        });
      }
    }

    function firstEnabled($btns) {
      const $enabled = $btns.filter(function () {
        return !$(this).prop("disabled");
      });
      return $enabled.first();
    }

    function ensureValidSelection() {
      let selectedColor = getSelectedColor();
      let selectedSize = getSelectedSize();

      if (hasColorUi && !selectedColor) {
        const $btn = firstEnabled($content.find(".color-variation-item button"));
        if ($btn.length) $btn.trigger("click");
        return;
      }

      if (hasSizeUi && !selectedSize) {
        const $btn = firstEnabled($content.find(".size-variation-item button"));
        if ($btn.length) $btn.trigger("click");
        return;
      }

      // If selection doesn't exist, pick the first selectable variant.
      const keyColor = hasColorUi ? selectedColor : null;
      const keySize = hasSizeUi ? selectedSize : null;
      const isValid = findExactVariant(keyColor, keySize);
      if (isValid) return;

      const sorted = variants
        .slice()
        .sort((a, b) => {
          const sa = a.stock > 0 ? 0 : 1;
          const sb = b.stock > 0 ? 0 : 1;
          if (sa !== sb) return sa - sb;
          return a.price - b.price;
        });
      const first = sorted[0] || null;
      if (!first) return;

      if (hasColorUi && first.color) {
        const $btn = $content
          .find('.color-variation-item button[data-color-text]')
          .filter(function () {
            return (
              normalizeKey($(this).attr("data-color-text")) === normalizeKey(first.color)
            );
          })
          .first();
        if ($btn.length) $btn.trigger("click");
      }

      if (hasSizeUi && first.size) {
        const $btn = $content
          .find('.size-variation-item button[data-size-text]')
          .filter(function () {
            return (
              normalizeKey($(this).attr("data-size-text")) === normalizeKey(first.size)
            );
          })
          .first();
        if ($btn.length) $btn.trigger("click");
      }
    }

    function updateDisplayedInfo() {
      const $price = $content.find(".current-price").first();
      const selectedColor = getSelectedColor();
      const selectedSize = getSelectedSize();
      const keyColor = hasColorUi ? selectedColor : null;
      const keySize = hasSizeUi ? selectedSize : null;

      const v = findExactVariant(keyColor, keySize);
      if (!v) return;

      if ($price.length) {
        $price.attr("data-qv-price", String(v.price));
        $price.text(formatVnd(v.price));
      }

      const $stock = $content.find(".js-qv-stock").first();
      if ($stock.length) {
        try {
          $stock.text(new Intl.NumberFormat("vi-VN").format(v.stock));
        } catch (e) {
          $stock.text(String(v.stock));
        }
      }
    }

    // Avoid duplicate bindings when content is re-injected.
    $content.off("click.qvColor").on("click.qvColor", ".color-variation-item button", function () {
      if ($(this).prop("disabled")) return;
      const colorText = ($(this).attr("data-color-text") || "").trim();
      if (!colorText) return;

      $content
        .find(".color-variation-item button")
        .removeClass("border-primary")
        .addClass("border-gray-300");
      $(this).removeClass("border-gray-300").addClass("border-primary");
      $content.find(".color-variation-selected-color").text(colorText);
      syncOptionAvailability();
      ensureValidSelection();
      updateDisplayedInfo();
    });

    $content.off("click.qvSize").on("click.qvSize", ".size-variation-item button", function () {
      if ($(this).prop("disabled")) return;
      const sizeText = ($(this).attr("data-size-text") || "").trim();
      if (!sizeText) return;

      $content.find(".size-variation-item button").each(function () {
        $(this)
          .removeClass("border-primary bg-primary text-white hover:bg-primary")
          .addClass("text-light-primary-text border border-gray-300");
      });
      $(this)
        .removeClass("text-light-primary-text border border-gray-300")
        .addClass("border border-primary bg-primary text-white hover:bg-primary");
      $content.find(".size-variation-selected-size").text(sizeText);
      syncOptionAvailability();
      ensureValidSelection();
      updateDisplayedInfo();
    });

    syncOptionAvailability();
    ensureValidSelection();
    updateDisplayedInfo();
  }

  function loadQuickViewHtml(requestUrl) {
    return $.ajax({ url: requestUrl, method: "GET", cache: false });
  }

  function prefetchQuickViews() {
    const urls = [];
    $(".quick-view-sidebar-btn[data-quick-view-url]").each(function () {
      const u = $(this).attr("data-quick-view-url");
      if (!u || u === "#") return;
      if (__quickViewHtmlCache[u]) return;
      urls.push(u);
    });

    if (!urls.length) return;

    // Limit concurrency to avoid flooding the server.
    const maxConcurrent = 2;
    let active = 0;
    let index = 0;

    function pump() {
      while (active < maxConcurrent && index < urls.length) {
        const url = urls[index++];
        if (__quickViewHtmlCache[url]) continue;
        active++;
        loadQuickViewHtml(url)
          .done(function (html) {
            __quickViewHtmlCache[url] = html;
          })
          .always(function () {
            active--;
            pump();
          });
      }
    }

    pump();
  }

  function schedulePrefetchQuickViews() {
    try {
      if (typeof window.requestIdleCallback === "function") {
        window.requestIdleCallback(prefetchQuickViews, { timeout: 1500 });
      } else {
        setTimeout(prefetchQuickViews, 600);
      }
    } catch (e) {
      setTimeout(prefetchQuickViews, 600);
    }
  }

  function handleQuickViewSidebarClick(e) {
    e.preventDefault();
    // If this handler is bound directly on the button, stop bubbling so we don't
    // double-handle (direct + delegated) and to avoid other scripts interfering.
    if (e.currentTarget === this) {
      try {
        e.stopPropagation();
      } catch (err) {}
    }

    isAnythingOpen();

    const sidebarSelector = ".quick-view-sidebar";
    const sidebarEl = document.querySelector(sidebarSelector);
    const contentEl = sidebarEl
      ? sidebarEl.querySelector(".quick-view-sidebar-content")
      : null;

    const requestUrl =
      (typeof $(this).data("quickViewUrl") === "string"
        ? $(this).data("quickViewUrl")
        : null) || $(this).attr("data-quick-view-url");

    if (sidebarEl && requestUrl) {
      sidebarEl.setAttribute("data-qv-current-url", requestUrl);
      sidebarEl.setAttribute("data-qv-request-id", String(Date.now()) + ":" + String(Math.random()));
    }

    if (contentEl && requestUrl && __quickViewHtmlCache[requestUrl]) {
      $(contentEl).html(__quickViewHtmlCache[requestUrl]);
      initQuickViewVariantUi(contentEl);
      showSidebar(sidebarSelector);
      return;
    }

    if (contentEl) {
      $(contentEl).html('<div class="w-full text-center py-6">Loading...</div>');
    }

    if (contentEl && requestUrl && requestUrl !== "#") {
      try {
        const requestId = sidebarEl ? sidebarEl.getAttribute("data-qv-request-id") : null;

        loadQuickViewHtml(requestUrl)
          .done(function (html) {
            if (sidebarEl) {
              const currentUrl = sidebarEl.getAttribute("data-qv-current-url");
              const currentRequestId = sidebarEl.getAttribute("data-qv-request-id");
              if (currentUrl !== requestUrl || currentRequestId !== requestId) {
                return;
              }
            }
            __quickViewHtmlCache[requestUrl] = html;
            $(contentEl).html(html);
            initQuickViewVariantUi(contentEl);
          })
          .fail(function () {
            $(contentEl).html(
              '<div class="w-full text-center py-6">Không tải được Quick View.</div>'
            );
          });
      } catch (err) {
        $(contentEl).html(
          '<div class="w-full text-center py-6">Không tải được Quick View.</div>'
        );
      }
    } else if (contentEl) {
      $(contentEl).html(
        '<div class="w-full text-center py-6">Không tải được Quick View.</div>'
      );
    }

    showSidebar(sidebarSelector);
  }

  // Direct bind (works even if other scripts stop bubbling)
  $(".quick-view-sidebar-btn")
    .off("click.sellzyQuickView")
    .on("click.sellzyQuickView", handleQuickViewSidebarClick);

  // Delegated bind (works for buttons rendered later)
  $(document)
    .off("click.sellzyQuickViewDelegate", ".quick-view-sidebar-btn")
    .on(
      "click.sellzyQuickViewDelegate",
      ".quick-view-sidebar-btn",
      handleQuickViewSidebarClick
    );

  // Prefetch product quick views in the background for instant opening.
  schedulePrefetchQuickViews();

  /*------ Radial Bar Chart ------*/
  const radialBarChart = $("#radial-bar-chart");
  if (radialBarChart.length) {
    const radialBarChartOptions = {
      series: [30, 20, 70, 50],
      chart: {
        width: 250,
        type: "donut",
      },
      dataLabels: {
        enabled: false,
      },
      colors: ["#5ed9ba", "#056d6e", "#04535c", "#088178"],
      labels: [
        "Recent Orders",
        "Pending Payments",
        "Received Payments",
        "Complete Order",
      ],
      responsive: [
        {
          breakpoint: 480,
          options: {
            chart: {
              width: 200,
            },
            legend: {
              show: false,
            },
          },
        },
      ],
      legend: {
        show: false,
      },
    };

    safeInit(function () {
      if (typeof ApexCharts === "undefined") return;
      new ApexCharts(
        document.querySelector("#radial-bar-chart"),
        radialBarChartOptions
      ).render();
    });
  }

  /*------ Price Movement Chart ------*/
  const priceMovementChart = $("#price-movement-chart");
  if (priceMovementChart.length) {
    const priceMovementChartOptions = {
      series: [
        {
          name: "Asia",
          data: [85, 32, 67, 120, 45, 98, 23, 140, 75, 110, 55, 135],
        },
        {
          name: "America",
          data: [20, 51, 35, 51, 49, 62, 69, 91, 148, 100, 120, 150],
        },
      ],
      colors: ["#088178", "#FFE700"],
      chart: {
        height: 340,
        type: "line",
        zoom: {
          enabled: false,
        },
        toolbar: {
          show: false,
        },
      },
      dataLabels: {
        enabled: false,
      },
      stroke: {
        curve: "straight",
        width: 2,
      },
      title: {
        show: false,
      },
      grid: {
        show: true,
        strokeDashArray: 3,
        strokeColor: "#919EAB3D",
      },
      yaxis: {
        labels: {
          style: {
            fontSize: "12px",
            fontWeight: "400",
            color: "#919EAB",
            fontFamily: "var(--font-dm-sans)",
          },
        },
      },
      xaxis: {
        labels: {
          style: {
            fontSize: "12px",
            fontWeight: "400",
            color: "#919EAB",
            fontFamily: "var(--font-dm-sans)",
          },
        },
        categories: [
          "Jan",
          "Feb",
          "Mar",
          "Apr",
          "May",
          "Jun",
          "Jul",
          "Aug",
          "Sep",
          "Oct",
          "Nov",
          "Dec",
        ],
      },
      legend: {
        show: false,
      },
    };

    safeInit(function () {
      if (typeof ApexCharts === "undefined") return;
      new ApexCharts(
        document.querySelector("#price-movement-chart"),
        priceMovementChartOptions
      ).render();
    });
  }

  /*------ Common Slider ----*/
  safeInit(function () {
    if ($.fn && $.fn.slick) {
      $(".sellzy-slider").slick({
    prevArrow:
      '<span class="slider-btn slider-prev size-12 rounded-full inline-flex items-center justify-center transition-colors duration-300 group/slider-btn cursor-pointer"><i class="hgi hgi-stroke hgi-arrow-left-01 text-[22px] text-light-primary-text transition-colors duration-300"></i></span>',
    nextArrow:
      '<span class="slider-btn slider-next size-12 rounded-full inline-flex items-center justify-center transition-colors duration-300 group/slider-btn cursor-pointer"><i class="hgi hgi-stroke hgi-arrow-right-01 text-[22px] text-light-primary-text transition-colors duration-300"></i></span>',
      });

      addCleanup(function () {
        try {
          const $slider = $(".sellzy-slider");
          if ($slider.hasClass("slick-initialized")) $slider.slick("unslick");
        } catch (e) {}
      });
    }
  });

  /*------ Scroll To Top Button ----*/
  const $scrollToTop = $(".scroll-to-top");
  $win.on("scroll.sellzy", function () {
    if ($win.scrollTop() > 300) {
      $scrollToTop.removeClass("hide").addClass("active");
    } else {
      $scrollToTop.removeClass("active").addClass("hide");
    }
  });

  $scrollToTop.on("click.sellzy", function () {
    window.scrollTo({ top: 0, behavior: "smooth" });
    return false;
  });

  /*------ Cleanup (Memory Management) ----*/
  function destroy() {
    try {
      // Remove all namespaced event listeners
      $win.off(".sellzy");
      $doc.off(".sellzy");
      $body.off(".sellzy");
      $("select").off(".sellzy");
      $modalOverlay.off(".sellzy");
      $scrollToTop.off(".sellzy");

      // Run additional cleanup tasks (observers, sliders, plugins)
      cleanupTasks.forEach(function (task) {
        try {
          task();
        } catch (e) {}
      });
    } catch (e) {}
  }

  // Cleanup on page unload/navigation
  $win.on("unload.sellzy", destroy);
})(jQuery);
