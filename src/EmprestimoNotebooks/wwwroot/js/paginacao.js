const listasPaginadas = Array.from(document.querySelectorAll('[data-paginacao]'));
let pedidoDePagina;

function atualizarLinksDaLista(lista, paginas) {
    for (const link of lista.querySelectorAll('.paginacao a, [data-paginacao-retorno]')) {
        const destino = new URL(link.href);
        for (const outraLista of paginas) {
            const parametro = outraLista.dataset.paginacao;
            if (parametro === lista.dataset.paginacao) continue;

            const pagina = outraLista.dataset.paginaAtual;
            if (pagina === '1') {
                destino.searchParams.delete(parametro);
            } else {
                destino.searchParams.set(parametro, pagina);
            }
        }
        link.href = destino.href;
    }
}

async function carregarPagina(destino, linkClicado) {
    pedidoDePagina?.abort();
    const pedido = new AbortController();
    pedidoDePagina = pedido;
    const listaClicada = linkClicado?.closest('[data-paginacao]');

    try {
        const resposta = await fetch(destino, { signal: pedido.signal });
        if (!resposta.ok || resposta.redirected) throw new Error();

        const html = await resposta.text();
        if (pedido.signal.aborted) return;

        const pagina = new DOMParser().parseFromString(html, 'text/html');
        const novasListas = listasPaginadas.map(lista =>
            pagina.querySelector(`[data-paginacao="${lista.dataset.paginacao}"]`));
        if (novasListas.some(lista => !lista)) throw new Error();

        const manterFoco = listaClicada?.contains(document.activeElement);
        if (linkClicado) history.pushState(null, '', destino);

        for (const [indice, lista] of listasPaginadas.entries()) {
            const nova = novasListas[indice];
            if (lista === listaClicada || lista.dataset.paginaAtual !== nova.dataset.paginaAtual) {
                lista.replaceChildren(...nova.childNodes);
                lista.dataset.paginaAtual = nova.dataset.paginaAtual;
            } else {
                atualizarLinksDaLista(lista, novasListas);
            }
        }

        if (manterFoco) {
            listaClicada.querySelector('[aria-current="page"]')?.focus({ preventScroll: true });
        }
    } catch {
        if (pedido.signal.aborted) return;
        if (linkClicado) {
            location.assign(destino);
        } else {
            location.reload();
        }
    }
}

document.addEventListener('click', evento => {
    const link = evento.target.closest('.paginacao a');
    if (!link || evento.defaultPrevented || evento.button !== 0 || evento.metaKey || evento.ctrlKey || evento.shiftKey || evento.altKey) return;

    evento.preventDefault();
    carregarPagina(link.href, link);
});

window.addEventListener('popstate', () => carregarPagina(location.href));
