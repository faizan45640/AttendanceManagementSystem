// Universal AJAX Form Handler for Add/Edit Operations
// This script handles form submissions via AJAX for all CRUD forms

$(document).ready(function () {
    // Handle all forms with class 'ajax-form'
    $('.ajax-form').on('submit', function (e) {
        e.preventDefault();

        if (!$(this).valid()) {
            return false;
        }

        const $form = $(this);
        const $submitBtn = $form.find('button[type="submit"]');
        const originalBtnText = $submitBtn.html();

        // Show loading state
        $submitBtn.prop('disabled', true).html(`
            <svg class="mr-2 h-5 w-5 animate-spin inline" fill="none" viewBox="0 0 24 24">
                <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
            </svg>
            Processing...
        `);

        // Clear previous alerts
        $('.alert-container').remove();

        $.ajax({
            url: $form.attr('action'),
            type: 'POST',
            data: $form.serialize(),
            success: function (response) {
                if (response.success) {
                    showAlert('success', response.message);

                    // Clear form for add operations
                    if ($form.data('clear-on-success') !== false) {
                        $form[0].reset();
                    }

                    // Redirect if needed
                    if ($form.data('redirect-url')) {
                        setTimeout(() => {
                            window.location.href = $form.data('redirect-url');
                        }, 1500);
                    }
                } else {
                    showAlert('error', response.message || 'An unexpected error occurred.');
                }
            },
            error: function (xhr) {
                let errorMessage = 'An error occurred while processing your request.';

                if (xhr.responseJSON) {
                    if (xhr.responseJSON.message) {
                        errorMessage = xhr.responseJSON.message;
                    }

                    // Handle validation errors
                    if (xhr.responseJSON.errors) {
                        const errors = xhr.responseJSON.errors;
                        for (const field in errors) {
                            if (errors.hasOwnProperty(field)) {
                                const $input = $(`input[name="${field}"], select[name="${field}"], textarea[name="${field}"]`);
                                const $errorSpan = $input.siblings('span[data-valmsg-for]');
                                if ($errorSpan.length && errors[field].length > 0) {
                                    $errorSpan.text(errors[field][0]);
                                    $input.addClass('border-red-500');
                                }
                            }
                        }
                    }
                }

                showAlert('error', errorMessage);
            },
            complete: function () {
                $submitBtn.prop('disabled', false).html(originalBtnText);
            }
        });
    });

    // Clear validation errors on input
    $('input, select, textarea').on('input change', function () {
        const $input = $(this);
        const $errorSpan = $input.siblings('span[data-valmsg-for]');
        if ($errorSpan.length) {
            $errorSpan.text('');
        }
        $input.removeClass('border-red-500');
    });
});

// Alert display function
function showAlert(type, message) {
    $('.alert-container').remove();

    const isSuccess = type === 'success';
    const alertHtml = `
        <div class="alert-container mb-4 rounded-xl border ${isSuccess ? 'border-success-500 bg-success-50 dark:border-success-500/30 dark:bg-success-500/15' : 'border-error-500 bg-error-50 dark:border-error-500/30 dark:bg-error-500/15'} p-4">
            <div class="flex items-start gap-3">
                <div class="-mt-0.5 ${isSuccess ? 'text-success-500' : 'text-error-500'}">
                    <svg class="fill-current" width="24" height="24" viewBox="0 0 24 24">
                        ${isSuccess ?
            '<path fill-rule="evenodd" clip-rule="evenodd" d="M3.70186 12.0001C3.70186 7.41711 7.41711 3.70186 12.0001 3.70186C16.5831 3.70186 20.2984 7.41711 20.2984 12.0001C20.2984 16.5831 16.5831 20.2984 12.0001 20.2984C7.41711 20.2984 3.70186 16.5831 3.70186 12.0001ZM15.6197 10.7395C15.9712 10.388 15.9712 9.81819 15.6197 9.46672C15.2683 9.11525 14.6984 9.11525 14.347 9.46672L11.1894 12.6243L9.6533 11.0883C9.30183 10.7368 8.73198 10.7368 8.38051 11.0883C8.02904 11.4397 8.02904 12.0096 8.38051 12.3611L10.553 14.5335C10.7217 14.7023 10.9507 14.7971 11.1894 14.7971C11.428 14.7971 11.657 14.7023 11.8257 14.5335L15.6197 10.7395Z" fill=""></path>' :
            '<path fill-rule="evenodd" clip-rule="evenodd" d="M12 3.70186C7.41711 3.70186 3.70186 7.41711 3.70186 12C3.70186 16.5829 7.41711 20.2981 12 20.2981C16.5829 20.2981 20.2981 16.5829 20.2981 12C20.2981 7.41711 16.5829 3.70186 12 3.70186ZM12 7.40186C12.4971 7.40186 12.9 7.80476 12.9 8.30186V12.6019C12.9 13.099 12.4971 13.5019 12 13.5019C11.5029 13.5019 11.1 13.099 11.1 12.6019V8.30186C11.1 7.80476 11.5029 7.40186 12 7.40186ZM12 17.1019C12.6627 17.1019 13.2 16.5646 13.2 15.9019C13.2 15.2392 12.6627 14.7019 12 14.7019C11.3373 14.7019 10.8 15.2392 10.8 15.9019C10.8 16.5646 11.3373 17.1019 12 17.1019Z" fill=""></path>'
        }
                    </svg>
                </div>
                <div>
                    <h4 class="mb-1 text-sm font-semibold text-gray-800 dark:text-white/90">${isSuccess ? 'Success' : 'Error'}</h4>
                    <p class="text-sm text-gray-500 dark:text-gray-400">${message}</p>
                </div>
            </div>
        </div>
    `;

    // Insert alert at top of page or in alert container
    if ($('#alertContainer').length) {
        $('#alertContainer').html(alertHtml);
    } else {
        $('.rounded-lg.border.border-gray-200.bg-white').first().before(alertHtml);
    }

    $('html, body').animate({ scrollTop: 0 }, 300);

    if (isSuccess) {
        setTimeout(() => $('.alert-container').fadeOut(300, function () { $(this).remove(); }), 5000);
    }
}
