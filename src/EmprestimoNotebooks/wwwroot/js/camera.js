const camera = document.querySelector("[data-camera]");
const arquivo = camera.querySelector('input[type="file"]');
const video = camera.querySelector("video");
const previa = camera.querySelector("img");
const statusCamera = camera.querySelector('[role="status"]');
const abrirCamera = camera.querySelector("[data-abrir-camera]");
const tirarFoto = camera.querySelector("[data-tirar-foto]");
const salvar = arquivo.form.querySelector('button[type="submit"]');
const aluno = arquivo.form.querySelector("[data-camera-aluno]");

let fluxo;
let enderecoFoto;
let sessao = 0;
const capturaDisponivel = navigator.mediaDevices?.getUserMedia && typeof DataTransfer === "function";

function pararCamera() {
    fluxo?.getTracks().forEach(trilha => trilha.stop());
    fluxo = null;
    video.srcObject = null;
    video.hidden = true;
    tirarFoto.hidden = true;
    abrirCamera.hidden = false;
}

function limparFoto() {
    if (enderecoFoto) {
        URL.revokeObjectURL(enderecoFoto);
        enderecoFoto = null;
    }

    previa.removeAttribute("src");
    previa.hidden = true;
    arquivo.value = "";
    salvar.disabled = true;
    abrirCamera.textContent = "Abrir câmera";
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

abrirCamera.addEventListener("click", async () => {
    const atual = ++sessao;
    limparFoto();
    abrirCamera.disabled = true;
    statusCamera.textContent = "Abrindo a câmera…";

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
        if (atual !== sessao) {
            return;
        }

        abrirCamera.hidden = true;
        tirarFoto.hidden = false;
        tirarFoto.disabled = false;
        statusCamera.textContent = "Câmera aberta.";
        tirarFoto.focus();
    } catch (erro) {
        if (atual === sessao) {
            pararCamera();
            statusCamera.textContent = mensagemDaCamera(erro);
            abrirCamera.disabled = false;
            abrirCamera.focus();
        }
    } finally {
        if (atual === sessao) {
            abrirCamera.disabled = false;
        }
    }
});

tirarFoto.addEventListener("click", () => {
    if (!video.videoWidth || !video.videoHeight) {
        statusCamera.textContent = "Aguarde a imagem da câmera e tente novamente.";
        return;
    }

    const atual = sessao;
    const canvas = document.createElement("canvas");
    const escala = Math.min(1, 960 / Math.max(video.videoWidth, video.videoHeight));
    canvas.width = Math.round(video.videoWidth * escala);
    canvas.height = Math.round(video.videoHeight * escala);
    canvas.getContext("2d").drawImage(video, 0, 0, canvas.width, canvas.height);
    tirarFoto.disabled = true;

    canvas.toBlob(foto => {
        if (atual !== sessao) {
            return;
        }

        if (!foto || foto.type !== "image/jpeg") {
            statusCamera.textContent = "Não foi possível tirar a foto. Tente novamente.";
            tirarFoto.disabled = false;
            return;
        }

        const arquivos = new DataTransfer();
        arquivos.items.add(new File([foto], "foto.jpg", { type: "image/jpeg" }));
        arquivo.files = arquivos.files;
        enderecoFoto = URL.createObjectURL(foto);
        previa.src = enderecoFoto;
        previa.hidden = false;
        pararCamera();
        salvar.disabled = false;
        abrirCamera.textContent = "Refazer foto";
        statusCamera.textContent = "Foto pronta. Confira a prévia antes de salvar.";
        abrirCamera.focus();
    }, "image/jpeg", 0.85);
});

function reiniciarCamera() {
    sessao++;
    pararCamera();
    limparFoto();
    abrirCamera.hidden = !capturaDisponivel;
    abrirCamera.disabled = aluno !== null && !aluno.value;
    statusCamera.textContent = abrirCamera.disabled ? "Selecione o aluno para abrir a câmera." : "";
}

window.addEventListener("pagehide", reiniciarCamera);

if (capturaDisponivel) {
    aluno?.addEventListener("change", reiniciarCamera);
    reiniciarCamera();
} else {
    statusCamera.textContent = window.isSecureContext
        ? "Este navegador não oferece suporte à captura pela câmera."
        : "Abra o sistema em localhost ou HTTPS para usar a câmera.";
}
