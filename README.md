# Potion Pop! 🧪✨

Jogo mobile de **ordenar as cores das poções nas garrafas** (estilo *Magic Sort*), feito em **Unity 6000.6.3f1** com UI
montada por código (uGUI + TextMeshPro), com a mesma arquitetura e o mesmo design system do **Shelf Pop!**. Toda a arte
foi gerada com o **Codex CLI** (as garrafas de vidro são desenhadas por código a partir da mesma forma paramétrica que
o jogo usa para despejar o líquido) e todos os sons/músicas são sintetizados por código.

Mascote: **Luna**, a gatinha bruxa. Idiomas: **português, inglês e espanhol**.

## Como jogar

Toque numa garrafa para levantá-la e toque em outra para despejar: a cor de cima só cai numa garrafa vazia ou com a
mesma cor no topo (cabe até 4 doses por garrafa). Garrafa cheia de uma cor só ganha uma **rolha**. Separe todas as cores!

- **Cores escondidas "?"** (a partir da fase 15) — só aparecem quando ficam no topo.
- **Garrafas de pedra** (a partir da fase 25) — com contador: cada garrafa completada racha a pedra; no zero ela quebra
  (ou quebre na hora assistindo a um anúncio).
- **Estrelas**: 1 a 3 por fase conforme o número de jogadas; **combo** ao completar garrafas em sequência.
- **Fases infinitas** geradas por código e **sempre verificadas como solucionáveis** (solucionador A*); fases difíceis
  (caveira) a cada 10.
- **6 mundos** de 20 fases (depois repetem): Floresta Encantada, Cavernas de Cristal, Reino dos Doces, Castelo nas
  Nuvens, Lagoa de Coral e Vila do Luar — com a tela de **Mundos** (mapa de fases, estrelas e rejogar fases).

### Reforços (boosters)
| Reforço | Libera | O que faz |
|---|---|---|
| Desfazer | fase 3 | volta a última jogada |
| Embaralhar | fase 6 | embaralha as cores das garrafas abertas |
| Garrafa Extra | fase 8 | +1 garrafa vazia (até 2 por fase) |
| Poção Arco-íris | fase 10 | antes da fase: tira uma cor inteira |
| Varinha Mágica | fase 12 | tira da mesa a cor mais enterrada |
| Bola de Cristal | fase 15 | antes da fase: revela todas as cores escondidas |

### Meta
Vidas (5, uma a cada 30 min), moedas, estrelas, **sequência de vitórias** (dá reforços grátis), **prêmio diário**,
**roleta da sorte**, **baú de estrelas** (a cada 30 estrelas), **missões diárias**, **coleção de cartas** (um álbum de 9
cartas mágicas por mundo), **ranking semanal e global**, perfil com avatar, loja e **login com Google/Apple** para salvar
na nuvem (Firebase).

## Como abrir

1. Abra a pasta no Unity Hub com o Unity **6000.6.3f1**.
2. Menu **Potion Pop ▸ Rebuild Project** (fonte Lilita One, cena, ServicesConfig e configurações do app).
3. Abra `Assets/_Game/Scenes/Main.unity` e dê Play em retrato (ex.: 1080×2340).

Menu **Potion Pop** no editor: Rebuild Project, Validate Levels 1-500, Build (Android APK/AAB, iOS, macOS), Capture
Screenshot, QA Tour e Debug (moedas, reforços, vidas, pular/vencer fase, apagar save).

Testes: **Window ▸ General ▸ Test Runner ▸ EditMode** (regras das garrafas, solucionador, fases 1–300, save, economia,
idiomas, serviços) e **PlayMode** (toques rápidos, despejos paralelos, fila por garrafa, reentrância, pedras,
desfazer, cancelamento e vitória). Jogadas rápidas são validadas no estado já confirmado: animações de garrafas
independentes começam juntas; as que usam a mesma garrafa seguem a ordem dos toques.

O teste visual de PlayMode usa o `CaptureTool` existente quando `POTIONPOP_QA_CAPTURE_DIR` aponta para uma pasta
de capturas. Execute com renderização habilitada (sem `-nographics`); ele usa save em memória e não altera o progresso
real. Sem a variável, a parte de captura é ignorada.

## Contas, nuvem e anúncios

- **Firebase** (Auth por REST + Firestore) para login e save na nuvem — configurado pela CLI, ver
  [`Docs/Firebase-Setup.md`](Docs/Firebase-Setup.md); regras em `firebase/firestore.rules`.
- **AdMob** (Google Mobile Ads + UMP): banner na fase, intersticial entre fases (a partir da 6) e premiados (continuar
  com +1 garrafa, dobrar moedas, vida extra, giro extra, moedas na loja, quebrar pedra). Usa os **IDs de teste do
  Google** até os blocos reais serem criados.
- Bundle id: `br.com.brunogames.potionpop`

## Estrutura

```
Assets/_Game/
  Scripts/
    Core/         save, economia, vidas, progresso (estrelas por fase), prêmio diário, roleta, baú, missões, coleção,
                  idiomas, áudio, vibração, arte, paleta das poções (Liquids)
    Levels/       regras das garrafas (BoardState), gerador, solucionador A* e dificuldade — C# puro, testável
    UI/           design system em código (Framework/), telas (Home, Mundos, Loja, Ranking, Coleção, Perfil), popups
    Game/         tela da partida, sessão, HUD, reforços, tutorial, popups da fase; Board/ = garrafas, líquido e despejo
    Services/     Firebase (auth + Firestore), ranking, AdMob
  Resources/      Art/ (sprites + art_index.json), Audio/, Loc/*.csv (key,en,pt,es), catalog.json, bottle_shape.json
Docs/             GDD.md, DesignSystem.md, Firebase-Setup.md, STATUS.md
Tools/            pipeline de arte (Codex), processamento, catálogo, áudio, checagem de compilação
ArtSource/raw/    imagens originais geradas pelo Codex
```

## Pipeline de conteúdo

```bash
python3 Tools/gen_art.py -j 10                                                   # gera no Codex as imagens que faltam
uv run --with pillow --with numpy --with scipy python Tools/process_art.py       # recorta/redimensiona + garrafas → Resources/Art
uv run --with pillow --with numpy --with scipy python Tools/export_catalog.py    # mundos + cartas (3 idiomas)
uv run --with pillow --with numpy --with scipy python Tools/gen_audio.py         # sintetiza efeitos e músicas
Tools/compile_check.sh                                                           # compila o C# em ~5 s sem abrir o Unity
```

## Ícones Android e build local

O launcher Android usa a arte original de Luna em `Assets/_Game/Art/AppIcon/app_icon.png`,
a mesma de `Docs/loja/imagens/icone_512.png`. `Tools/release/android_icons.py` deriva as camadas
adaptativas com margem transparente, sem recriar a ilustração. `ApplyAppIcons` preenche todos os slots
Android; `BuildIdentityGuard` rejeita camadas ausentes/incorretas, e o pós-processador gera o ícone
opaco de compatibilidade em seis densidades (Unity 6000.6 expõe somente slots adaptativos).

```bash
python3 Tools/release/build_release.py android
uv run --with pillow python Tools/release/validate_android_icons.py Builds/release/PotionPop-1.0.0-2.aab --code 2
```

O pipeline Android deriva as camadas antes de sincronizar a cópia do projeto e valida os pixels de todas
as 18 imagens empacotadas antes de aceitar o build. Para AAB, verifica também estrutura, pacote,
código de versão, referência do launcher no manifesto e assinatura. A chave continua vindo do setup existente.
`Assets/_Game/Tests/EditorBuild` cobre builds com foreground vazio ou background errado.

Em 2026-10-05, o AAB Android 1.0.0/código 2 foi gerado e validado localmente para corrigir o launcher
Unity do código 1. Inclui as jogadas rápidas, passou 124 testes EditMode e 18 PlayMode e foi instalado
no emulador Pixel_10. O certificado é o mesmo do AAB anterior. Nenhum upload/envio à loja foi feito;
a versão e o build iOS continuam 1.0.0/1.
