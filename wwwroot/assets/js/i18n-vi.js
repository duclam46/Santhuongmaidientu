/* Simple client-side Vietnamese translations for the static template.
   This avoids having to touch every .cshtml string.
*/
(function () {
  const exactMap = new Map([
    ["Home", "Trang chủ"],
    ["Home 1", "Trang chủ 1"],
    ["Home 2", "Trang chủ 2"],
    ["Home 3", "Trang chủ 3"],
    ["Home 4", "Trang chủ 4"],
    ["Home 5", "Trang chủ 5"],
    ["About Us", "Giới thiệu"],
    ["Shop", "Cửa hàng"],
    ["Product Details", "Chi tiết sản phẩm"],
    ["Blog", "Blog"],
    ["Contact", "Liên hệ"],

    ["Search for the Items", "Tìm kiếm sản phẩm"],
    ["Recent Search", "Tìm kiếm gần đây"],

    ["Cart", "Giỏ hàng"],
    ["Cart Products", "Sản phẩm trong giỏ"],
    ["Similar Products", "Sản phẩm tương tự"],

    ["My Account", "Tài khoản"],
    ["My Order", "Đơn hàng"],
    ["Wishlist", "Yêu thích"],

    ["Sales", "Giảm giá"],
    ["SALES", "GIẢM GIÁ"],
    ["New Arrival", "Hàng mới"],

    ["Add to Cart", "Thêm vào giỏ"],
    ["Free Shipping", "Miễn phí vận chuyển"],
    ["Free Shipping:", "Miễn phí vận chuyển:"],
    ["Estimated Delivery Time 5-7 Days", "Thời gian giao dự kiến 5–7 ngày"],

    ["Categories:", "Danh mục:"],
    ["Categories", "Danh mục"],
    ["Description", "Mô tả"],

    ["Write a Review", "Viết đánh giá"],
    ["Add Comment", "Thêm bình luận"],
    ["Verified purchase", "Đã mua hàng"],
    ["was this review helpful to you?", "Đánh giá này có hữu ích không?"],

    ["Privacy Policy", "Chính sách bảo mật"],

    ["Ends in:", "Kết thúc sau:"],
  ]);

  const exactRegexRules = [
    { re: /^Thank\s*\((\d+)\)$/, to: (m) => `Hữu ích (${m[1]})` },
    { re: /^Dislike\s*\((\d+)\)$/, to: (m) => `Không hữu ích (${m[1]})` },
    { re: /^(\d+)\s+Item\s+in\s+Cart$/i, to: (m) => `${m[1]} sản phẩm trong giỏ` },
    { re: /^(\d+)\s+Items\s+in\s+Cart$/i, to: (m) => `${m[1]} sản phẩm trong giỏ` },
  ];

  function translateExact(text) {
    if (!text) return null;
    const trimmed = text.trim();
    if (!trimmed) return null;

    if (exactMap.has(trimmed)) {
      return text.replace(trimmed, exactMap.get(trimmed));
    }

    for (const rule of exactRegexRules) {
      const match = trimmed.match(rule.re);
      if (match) {
        return text.replace(trimmed, rule.to(match));
      }
    }

    return null;
  }

  function shouldIgnoreTextNode(node) {
    if (!node || !node.parentElement) return true;
    const tag = node.parentElement.tagName;
    return tag === "SCRIPT" || tag === "STYLE" || tag === "NOSCRIPT";
  }

  function translateDocument() {
    if (!document || !document.body) return;

    // Translate text nodes
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    let current;
    while ((current = walker.nextNode())) {
      if (shouldIgnoreTextNode(current)) continue;
      const translated = translateExact(current.nodeValue);
      if (translated !== null) current.nodeValue = translated;
    }

    // Translate placeholders
    document.querySelectorAll("input[placeholder], textarea[placeholder]").forEach((el) => {
      const placeholder = el.getAttribute("placeholder");
      if (!placeholder) return;
      const translated = translateExact(placeholder);
      if (translated !== null) el.setAttribute("placeholder", translated.trim());
    });

    // Translate title attributes
    document.querySelectorAll("[title]").forEach((el) => {
      const title = el.getAttribute("title");
      if (!title) return;
      const translated = translateExact(title);
      if (translated !== null) el.setAttribute("title", translated.trim());
    });
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", translateDocument);
  } else {
    translateDocument();
  }
})();
