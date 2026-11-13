// Small helpers shared by every page.
(function () {
    // Dropdowns that apply as soon as they change.
    document.querySelectorAll("[data-autosubmit]").forEach(el =>
        el.addEventListener("change", () => el.form && el.form.submit()));

    // Ask before destructive actions.
    document.querySelectorAll("form[data-confirm]").forEach(f =>
        f.addEventListener("submit", e => { if (!confirm(f.dataset.confirm)) e.preventDefault(); }));

    // Typing in a filter box hides table rows that don't contain the text.
    document.querySelectorAll("input[data-filter]").forEach(input => {
        const table = document.querySelector(input.dataset.filter);
        if (!table) return;
        input.addEventListener("input", () => {
            const q = input.value.trim().toLowerCase();
            table.querySelectorAll("tbody tr").forEach(tr => {
                tr.style.display = !q || tr.textContent.toLowerCase().includes(q) ? "" : "none";
            });
        });
    });

    // "Select all" checkbox for a group of checkboxes.
    document.querySelectorAll("input[data-select-all]").forEach(all =>
        all.addEventListener("change", () =>
            document.querySelectorAll(all.dataset.selectAll).forEach(cb => {
                if (cb.closest("tr")?.style.display !== "none") cb.checked = all.checked;
            })));

    // Pages marked for auto-refresh reload every minute unless someone is typing.
    const refresh = document.body.querySelector("[data-autorefresh]");
    if (refresh) {
        setInterval(() => {
            const a = document.activeElement;
            if (a && ["INPUT", "SELECT", "TEXTAREA"].includes(a.tagName)) return;
            location.reload();
        }, 60000);
    }

    // Rows that open a link when clicked.
    document.querySelectorAll("tr[data-href]").forEach(tr => {
        tr.style.cursor = "pointer";
        tr.addEventListener("click", e => { if (!e.target.closest("a,button,input,select,form")) location.href = tr.dataset.href; });
    });

    // Forms that take a while show that they are working and can't be sent twice.
    document.querySelectorAll("form[data-busy]").forEach(f => f.addEventListener("submit", () => {
        const b = f.querySelector("button");
        if (b) { b.disabled = true; b.textContent = f.dataset.busy; }
    }));

    document.querySelectorAll("[data-print]").forEach(b => b.addEventListener("click", () => window.print()));
})();
