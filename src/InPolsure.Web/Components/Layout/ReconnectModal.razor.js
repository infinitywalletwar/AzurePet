// Reconnect UI logic from the .NET 10 Blazor Web App template, adapted for a dialog that exists only on
// interactive pages: this module is loaded on every page (App.razor), so it listens on the document instead of
// on the dialog element, which may be added later by enhanced navigation or be absent on Static SSR pages.
// blazor.web.js dispatches the non-bubbling "components-reconnect-state-changed" event on the dialog; a capturing
// listener on the document still receives it.

const modalId = "components-reconnect-modal";

function getReconnectModal() {
    return document.getElementById(modalId);
}

document.addEventListener("components-reconnect-state-changed", handleReconnectStateChanged, true);

document.addEventListener("click", (event) => {
    if (!(event.target instanceof Element)) {
        return;
    }

    if (event.target.closest("#components-reconnect-button")) {
        retry();
    } else if (event.target.closest("#components-resume-button")) {
        resume();
    }
});

function handleReconnectStateChanged(event) {
    const reconnectModal = event.target;
    if (!(reconnectModal instanceof HTMLDialogElement) || reconnectModal.id !== modalId) {
        return;
    }

    if (event.detail.state === "show") {
        if (!reconnectModal.open) {
            reconnectModal.showModal();
        }
    } else if (event.detail.state === "hide") {
        reconnectModal.close();
    } else if (event.detail.state === "failed") {
        document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
    } else if (event.detail.state === "rejected") {
        location.reload();
    }
}

async function retry() {
    document.removeEventListener("visibilitychange", retryWhenDocumentBecomesVisible);

    try {
        // Reconnect will asynchronously return:
        // - true to mean success
        // - false to mean we reached the server, but it rejected the connection (e.g., unknown circuit ID)
        // - exception to mean we didn't reach the server (this can be sync or async)
        const successful = await Blazor.reconnect();
        if (!successful) {
            // We have been able to reach the server, but the circuit is no longer available.
            // We'll reload the page so the user can continue using the app as quickly as possible.
            const resumeSuccessful = await Blazor.resumeCircuit();
            if (!resumeSuccessful) {
                location.reload();
            } else {
                getReconnectModal()?.close();
            }
        }
    } catch {
        // We got an exception, server is currently unavailable
        document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
    }
}

async function resume() {
    try {
        const successful = await Blazor.resumeCircuit();
        if (!successful) {
            location.reload();
        }
    } catch {
        getReconnectModal()?.classList.replace("components-reconnect-paused", "components-reconnect-resume-failed");
    }
}

async function retryWhenDocumentBecomesVisible() {
    if (document.visibilityState === "visible") {
        await retry();
    }
}
