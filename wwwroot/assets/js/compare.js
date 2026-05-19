/**
 * Premium Toast Notification System
 */
const Toast = {
    container: null,

    init() {
        if (!this.container) {
            this.container = document.createElement('div');
            this.container.className = 'toast-container';
            document.body.appendChild(this.container);
        }
    },

    show(message, type = 'success', duration = 3000) {
        this.init();

        const toast = document.createElement('div');
        toast.className = `toast-item toast-${type}`;

        const iconMap = {
            success: 'hgi-checkmark-circle-02',
            error: 'hgi-cancel-01',
            warning: 'hgi-alert-circle',
            info: 'hgi-information-circle'
        };

        const titleMap = {
            success: 'Thành công',
            error: 'Lỗi',
            warning: 'Cảnh báo',
            info: 'Thông báo'
        };

        toast.innerHTML = `
            <div class="toast-icon">
                <i class="hgi hgi-stroke ${iconMap[type] || iconMap.info}"></i>
            </div>
            <div class="toast-content">
                <div class="toast-title">${titleMap[type] || titleMap.info}</div>
                <div class="toast-message">${message}</div>
            </div>
            <div class="toast-close" onclick="this.parentElement.remove()">
                <i class="hgi hgi-stroke hgi-cancel-01"></i>
            </div>
            <div class="toast-progress">
                <div class="toast-progress-bar" style="animation: toast-progress ${duration}ms linear forwards"></div>
            </div>
        `;

        this.container.appendChild(toast);

        // Add progress bar animation
        const style = document.createElement('style');
        style.innerHTML = `@keyframes toast-progress { from { transform: scaleX(1); } to { transform: scaleX(0); } }`;
        document.head.appendChild(style);

        setTimeout(() => {
            toast.classList.add('closing');
            setTimeout(() => toast.remove(), 400);
        }, duration);
    },

    success(msg) { this.show(msg, 'success'); },
    error(msg) { this.show(msg, 'error'); },
    warning(msg) { this.show(msg, 'warning'); },
    info(msg) { this.show(msg, 'info'); }
};

/**
 * Product Comparison Logic
 */

async function addToCompare(productId) {
    try {
        const response = await fetch('/compare/add', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
            },
            body: `productId=${productId}`
        });

        if (!response.ok) {
            throw new Error('Network response was not ok');
        }

        const result = await response.json();
        if (result.success) {
            Toast.success(result.message);
            updateUI();
        } else {
            Toast.warning(result.message);
        }
    } catch (error) {
        console.error('Error adding to compare:', error);
        Toast.error('Đã có lỗi xảy ra khi thêm sản phẩm vào so sánh.');
    }
}

async function removeFromCompare(productId) {
    try {
        const response = await fetch('/compare/remove', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
            },
            body: `productId=${productId}`
        });

        const result = await response.json();
        if (result.success) {
            // Reload the page if we are on the compare page
            if (window.location.pathname.includes('/compare')) {
                window.location.reload();
            } else {
                Toast.success(result.message);
                updateUI();
            }
        }
    } catch (error) {
        console.error('Error removing from compare:', error);
    }
}

async function clearCompare() {
    if (!confirm('Bạn có chắc chắn muốn xóa tất cả sản phẩm trong danh sách so sánh?')) {
        return;
    }

    try {
        const response = await fetch('/compare/clear', {
            method: 'POST'
        });

        const result = await response.json();
        if (result.success) {
            if (window.location.pathname.includes('/compare')) {
                window.location.reload();
            } else {
                Toast.success(result.message);
                updateUI();
            }
        }
    } catch (error) {
        console.error('Error clearing compare:', error);
    }
}

function updateUI() {
    fetch('/compare/list')
        .then(res => res.json())
        .then(products => {
            const count = products.length;
            
            // Update all counters
            const counters = document.querySelectorAll('.compare-count');
            counters.forEach(el => {
                el.textContent = count;
                if (el.parentElement.classList.contains('hidden')) {
                     el.parentElement.classList.toggle('hidden', count === 0);
                }
            });

            // Update Floating Bar
            const compareBar = document.getElementById('compare-bar');
            const compareBarItems = document.getElementById('compare-bar-items');
            
            if (compareBar && compareBarItems) {
                if (count > 0) {
                    compareBar.classList.add('show');
                    
                    let itemsHtml = '';
                    products.forEach(p => {
                        itemsHtml += `
                            <div class="compare-item-mini group">
                                <img src="${p.imageUrl || '/assets/images/placeholder.png'}" alt="${p.name}">
                                <div class="remove-btn" onclick="removeFromCompare(${p.id})">
                                    <i class="hgi hgi-stroke hgi-cancel-01"></i>
                                </div>
                            </div>
                        `;
                    });
                    
                    // Add empty slots up to 4
                    for (let i = count; i < 4; i++) {
                        itemsHtml += `<div class="compare-item-empty"><i class="hgi hgi-stroke hgi-add-01"></i></div>`;
                    }
                    
                    compareBarItems.innerHTML = itemsHtml;
                } else {
                    compareBar.classList.remove('show');
                }
            }
        })
        .catch(console.error);
}

function toggleCompareBar(show) {
    const compareBar = document.getElementById('compare-bar');
    if (compareBar) {
        if (show) compareBar.classList.add('show');
        else compareBar.classList.remove('show');
    }
}

// Initialize on page load
document.addEventListener("DOMContentLoaded", function() {
    updateUI();
});
