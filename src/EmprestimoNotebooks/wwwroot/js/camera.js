const camposDeFoto = Array.from(document.querySelectorAll("[data-camera]"));
const formulario = camposDeFoto[0]?.closest("form");
const aluno = formulario?.querySelector("[data-camera-aluno]");
const salvar = formulario?.querySelector('button[type="submit"]');
const capturaDisponivel = navigator.mediaDevices?.getUserMedia && typeof DataTransfer === "function";
let cameraAtiva = null;

const cameras = camposDeFoto.map(prepararCamera);

function atualizarFormulario() {
    const faltaFoto = cameras.some(camera => !camera.arquivo.files.length);
    const faltaAluno = Boolean(aluno && !aluno.value);
    salvar.disabled = faltaFoto || faltaAluno || cameraAtiva !== null;

    for (const camera of cameras) {
        camera.abrir.disabled = faltaAluno || cameraAtiva !== null;
    }
}

function mensagemDaCamera(erro) {
    if (erro.name === "NotAllowedError") {
        return "Permita o acesso à câmera no navegador e tente novamente.";
    }

    if (erro.name === "NotFoundError") {
        return "Nenhuma câmera foi encontrada. Conecte uma câmera e tente novamente.";
    }

    return "Não foi possível abrir a câmera. Verifique se outro aplicativo está usando a câmera.";
}

function prepararCamera(campo) {
    const arquivo = campo.querySelector('input[type="file"]');
    const video = campo.querySelector("video");
    const previa = campo.querySelector("img");
    const status = campo.querySelector('[role="status"]');
    const abrir = campo.querySelector("[data-abrir-camera]");
    const tirar = campo.querySelector("[data-tirar-foto]");
    const fechar = campo.querySelector("[data-fechar-camera]");
    const camera = { arquivo, abrir, reiniciar };

    let fluxo;
    let enderecoFoto;
    let sessao = 0;

    function parar() {
        sessao++;
        fluxo?.getTracks().forEach(trilha => trilha.stop());
        fluxo = null;
        video.srcObject = null;
        video.hidden = true;
        tirar.hidden = true;
        fechar.hidden = true;
        abrir.hidden = !capturaDisponivel;
        previa.hidden = !enderecoFoto;
        if (cameraAtiva === camera) cameraAtiva = null;
    }

    function limparFoto() {
        if (enderecoFoto) URL.revokeObjectURL(enderecoFoto);
        enderecoFoto = null;
        previa.removeAttribute("src");
        previa.hidden = true;
        arquivo.value = "";
        abrir.textContent = "Abrir câmera";
    }

    function reiniciar() {
        parar();
        limparFoto();
        if (!capturaDisponivel) {
            status.textContent = window.isSecureContext
                ? "Este navegador não oferece suporte à captura pela câmera."
                : "Abra o sistema em localhost ou HTTPS para usar a câmera.";
        } else {
            status.textContent = aluno && !aluno.value ? "Selecione o aluno para abrir a câmera." : "";
        }
    }

    abrir.addEventListener("click", async () => {
        const atual = ++sessao;
        cameraAtiva = camera;
        atualizarFormulario();
        previa.hidden = true;
        abrir.hidden = true;
        fechar.hidden = false;
        status.textContent = "Abrindo a câmera…";
        fechar.focus();

        try {
            const novoFluxo = await navigator.mediaDevices.getUserMedia({ video: true, audio: false });
            if (atual !== sessao) {
                novoFluxo.getTracks().forEach(trilha => trilha.stop());
                return;
            }

            fluxo = novoFluxo;
            video.srcObject = fluxo;
            video.hidden = false;
            await video.play();
            if (atual !== sessao) return;

            tirar.hidden = false;
            tirar.disabled = false;
            status.textContent = "Câmera aberta.";
            tirar.focus();
        } catch (erro) {
            if (atual === sessao) {
                parar();
                atualizarFormulario();
                status.textContent = mensagemDaCamera(erro);
                abrir.focus();
            }
        }
    });

    tirar.addEventListener("click", () => {
        if (!video.videoWidth || !video.videoHeight) {
            status.textContent = "Aguarde a imagem da câmera e tente novamente.";
            return;
        }

        const atual = sessao;
        const canvas = document.createElement("canvas");
        const escala = Math.min(1, 960 / Math.max(video.videoWidth, video.videoHeight));
        canvas.width = Math.round(video.videoWidth * escala);
        canvas.height = Math.round(video.videoHeight * escala);
        canvas.getContext("2d").drawImage(video, 0, 0, canvas.width, canvas.height);
        tirar.disabled = true;

        canvas.toBlob(foto => {
            if (atual !== sessao) return;

            if (!foto || foto.type !== "image/jpeg" || foto.size > 1024 * 1024) {
                status.textContent = "Não foi possível tirar a foto. Tente novamente.";
                tirar.disabled = false;
                return;
            }

            limparFoto();
            const arquivos = new DataTransfer();
            arquivos.items.add(new File([foto], "foto.jpg", { type: "image/jpeg" }));
            arquivo.files = arquivos.files;
            enderecoFoto = URL.createObjectURL(foto);
            previa.src = enderecoFoto;
            parar();
            atualizarFormulario();
            abrir.textContent = "Refazer foto";
            status.textContent = "Foto pronta. Confira a prévia antes de salvar.";
            abrir.focus();
        }, "image/jpeg", 0.85);
    });

    fechar.addEventListener("click", () => {
        parar();
        atualizarFormulario();
        status.textContent = "Câmera fechada.";
        abrir.focus();
    });

    return camera;
}

function reiniciarCameras() {
    cameras.forEach(camera => camera.reiniciar());
    atualizarFormulario();
}

if (formulario) {
    aluno?.addEventListener("change", reiniciarCameras);
    window.addEventListener("pagehide", reiniciarCameras);
    reiniciarCameras();
}
