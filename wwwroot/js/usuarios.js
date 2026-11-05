document.querySelectorAll('[data-datos-personales]').forEach(panel => {
    const dni = panel.querySelector('[name="Dni"]');
    const nombres = panel.querySelector('[name="Nombres"]');
    const apellidos = panel.querySelector('[name="Apellidos"]');
    const button = panel.querySelector('[data-buscar-dni]');
    const status = panel.querySelector('[data-estado-dni]');
    button.addEventListener('click', async () => {
        const documento = dni.value.trim();
        if (!/^[0-9]{8}$/.test(documento)) {
            status.textContent = 'Ingrese un DNI de 8 dígitos.';
            dni.focus();
            return;
        }
        button.disabled = true;
        status.textContent = 'Consultando DNI…';
        const abort = new AbortController();
        const timeout = setTimeout(() => abort.abort(), 15000);
        try {
            const response = await fetch(`${panel.dataset.consultaUrl}?dni=${encodeURIComponent(documento)}`, { signal: abort.signal, headers: { Accept: 'application/json' } });
            if (!response.ok || response.redirected) throw new Error('Consulta no disponible');
            const data = await response.json();
            if (dni.value.trim() !== documento) { status.textContent = 'El DNI cambió. Consulta nuevamente.'; return; }
            if (!data.exito) { status.textContent = `${data.mensaje || 'No se encontraron datos.'} Puedes completar nombres y apellidos manualmente y guardar.`; return; }
            if (data.nombres && data.apellidos) {
                nombres.value = data.nombres;
                apellidos.value = data.apellidos;
                status.textContent = 'Datos encontrados. Revísalos antes de guardar.';
            } else {
                status.textContent = `El proveedor devolvió: ${data.nombreCompleto || 'sin nombre'}. Completa nombres y apellidos por separado.`;
            }
        } catch {
            status.textContent = 'No se pudo consultar el DNI. Verifica tu sesión o completa los datos manualmente.';
        } finally {
            clearTimeout(timeout);
            button.disabled = false;
        }
    });
});
