# Beat

Player de música leve para Windows. Busca no YouTube e toca só o áudio, sem navegador, e por isso usa bem menos memória RAM do que uma aba aberta.

## Baixar e usar

1. Abra a aba **Releases** deste repositório e baixe o **`Beat.exe`**.
2. Coloque o arquivo numa pasta própria (por exemplo `C:\Apps\Beat`) e abra.
3. Na primeira vez o Beat baixa o `yt-dlp.exe` sozinho, então precisa de internet.

> O Windows pode mostrar um aviso azul ("O Windows protegeu seu computador"), porque o programa não tem assinatura digital. Clique em **Mais informações** e depois em **Executar assim mesmo**.

## Recursos

- Busca por música ou artista, com a miniatura de cada vídeo
- Abas **Busca**, **Favoritos** e **Playlists**
- Botão direito na música: tocar, favoritar, adicionar à playlist, tocar músicas relacionadas
- **Relacionadas**: continua tocando músicas parecidas (Mix do YouTube) quando a lista acaba
- Aleatório, repetir (lista ou uma faixa), volume e barra de progresso
- Teclas de mídia do teclado funcionam com o app em segundo plano
- Ao fechar a janela tocando, o Beat vai para a bandeja do sistema
- Lembra a última busca, o volume, os favoritos e as playlists

## Atalhos

| Ação | Tecla |
| --- | --- |
| Buscar | Enter na caixa de busca |
| Tocar a música selecionada | Enter ou duplo clique |
| Pausar / continuar | Espaço |

## Compilar do código-fonte

Não precisa instalar nada: dê dois cliques em `build.bat`. Ele usa o compilador que já vem no Windows (.NET Framework 4) e gera o `Beat.exe`.

## Dados e requisitos

- Favoritos e playlists ficam em `%APPDATA%\Beat\dados.txt`.
- Requer Windows 10 ou 11 com o Windows Media Player (já vem instalado por padrão).
- O `yt-dlp` é atualizado sozinho a cada 7 dias. Se parar de funcionar, apague o `yt-dlp.exe` da pasta e abra o app de novo.

## Aviso

Projeto pessoal, sem vínculo com o YouTube. Use apenas para ouvir conteúdo de forma compatível com os termos de uso do serviço e com os direitos autorais.
