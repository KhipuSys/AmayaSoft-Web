(() => {
    const boton = document.getElementById('btnConsultarDocumento');
    if (!boton) return;
    const documento = document.getElementById('Documento');
    const nombre = document.getElementById('Nombre');
    const direccion = document.getElementById('Direccion');
    const mensaje = document.getElementById('consultaDocumentoMensaje');

    function informar(texto, error = false) {
        mensaje.textContent = texto;
        mensaje.classList.toggle('text-danger', error);
    }

    boton.addEventListener('click', async () => {
        const valor = documento.value.trim();
        if (!/^(?:[0-9]{8}|[0-9]{11})$/.test(valor)) {
            informar('Ingrese un DNI de 8 dígitos o un RUC de 11 dígitos.', true);
            documento.focus();
            return;
        }
        boton.disabled = true;
        informar('Consultando documento…');
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 15000);
        try {
            const response = await fetch(`${boton.dataset.consultaUrl}?documento=${encodeURIComponent(valor)}`, {
                signal: controller.signal, headers: { Accept: 'application/json' }
            });
            if (!response.ok || response.redirected) throw new Error('Consulta no disponible');
            const datos = await response.json();
            if (documento.value.trim() !== valor) {
                informar('El documento cambió durante la consulta. Pulsa Consultar nuevamente.', true);
                return;
            }
            if (!datos.exito || !datos.nombre) {
                informar(`${datos.mensaje || 'No se encontraron datos.'} Puedes completar el formulario manualmente y guardar.`, true);
                return;
            }
            nombre.value = datos.nombre;
            if (datos.direccion) direccion.value = datos.direccion;
            informar('Datos encontrados. Revisa el nombre o razón social y la dirección; guarda el formulario para aplicar los cambios.');
        } catch {
            informar('No se pudo consultar el documento. Verifica tu sesión o completa los datos manualmente.', true);
        } finally {
            clearTimeout(timeout);
            boton.disabled = false;
        }
    });
})();
