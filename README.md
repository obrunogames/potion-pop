# Potion Pop! 🛒✨

Jogo mobile de **organizar produtos nas prateleiras** (estilo *Goods Sort*), feito em **Unity 6000.6.3f1** com UI montada por código
(uGUI + TextMeshPro). Toda a arte foi gerada com o **Codex CLI** e todos os sons/músicas foram sintetizados por código.

Mascote: **Mimi**, a gatinha lojista. Idiomas: **português, inglês e espanhol**.

## Como jogar

Arraste um produto da frente de uma prateleira para um espaço vazio de outra. **3 produtos iguais** na mesma prateleira estouram.
Quando a frente de uma prateleira esvazia, a camada de trás vem para a frente. Limpe tudo antes do tempo acabar.

- **Prateleiras com camadas** (selo "+N"), **dispensers** (prateleira estreita com contador: só dá para tirar), **prateleiras trancadas**
  com corrente (abrem depois de N combinações ou assistindo a um anúncio).
- **Combo**: combinações em menos de 8 s multiplicam as estrelas (x2… x10) com "Boa!", "Ótimo!", "Incrível!", "Fantástico!".
- **Fases infinitas** geradas por código, **sempre verificadas como solucionáveis** antes de jogar; fases difíceis (caveira) a cada 10.
- **5 lojas temáticas** (20 fases cada, depois repetem): Mercadinho, Doceria, Loja de Brinquedos, Butique de Beleza e Hortifruti —
  60 produtos 3D.

### Reforços (boosters)
| Reforço | Libera | O que faz |
|---|---|---|
| Martelo | fase 3 | toque num item: ele e mais 2 iguais são esmagados |
| Varinha Mágica | fase 5 | completa sozinha o trio mais perto de fechar |
| Congelar | fase 7 | para o relógio por 15 s |
| Embaralhar | fase 9 | embaralha os itens das prateleiras abertas |
| +30s de Tempo | fase 6 | antes da fase |
| Bomba | fase 11 | antes da fase: limpa 2 trios |

### Meta
Vidas (5, uma a cada 30 min), moedas, estrelas, **sequência de vitórias** (dá reforços grátis), **prêmio diário** (7 dias),
**roleta da sorte**, **baú de estrelas** (1.000 estrelas), **missões diárias**, **coleção de cartas** (um álbum por loja),
**ranking semanal e global**, perfil com avatar, e **login com Google/Apple** para salvar na nuvem.

## Como abrir

1. Abra a pasta no Unity Hub com o Unity **6000.6.3f1**.
2. Menu **Potion Pop ▸ Rebuild Project** (importa o TMP, cria a fonte Lilita One, a cena, o ServicesConfig e as configurações do app).
3. Abra `Assets/_Game/Scenes/Main.unity` e dê Play. Use o Game View em retrato (ex.: 1080×2340).

### Menu Potion Pop no editor
| Item | O que faz |
|---|---|
| Rebuild Project | fonte, cena, ServicesConfig, Player Settings, templates Gradle, IDs do AdMob |
| Validate Levels 1-500 | gera e resolve as fases, relatório em `Logs/level_report.csv` |
| Build ▸ Android APK / AAB, iOS, macOS | builds em `Builds/` (assinatura Android pelas variáveis `POTIONPOP_KEYSTORE*`) |
| Capture Screenshot | captura 1080×2340 em `Screenshots/` |
| Debug ▸ … | moedas, reforços, vidas, pular fase, adiantar o relógio, apagar save, abrir o Design System |

Testes: **Window ▸ General ▸ Test Runner ▸ EditMode** (109 testes: regras do tabuleiro, solubilidade das fases 1–300, dicas,
save, economia, idiomas, serviços).

## Contas, nuvem e anúncios

- **Firebase** (Auth por REST + Firestore) para login Google/Apple e save na nuvem. Sem configuração, o jogo funciona offline e no editor
  o login é simulado. Passo a passo em [`Docs/Firebase-Setup.md`](Docs/Firebase-Setup.md); regras em `firebase/firestore.rules`.
- **AdMob** (Google Mobile Ads 11.5 + UMP/GDPR): banner na partida, intersticial entre fases (a partir da fase 6) e premiados
  (continuar, dobrar moedas, vida extra, giro extra, moedas na loja, abrir prateleira). Vem com os **IDs de teste do Google**:
  troque em `Assets/_Game/Resources/ServicesConfig.asset` antes de publicar.
- Bundle id: `br.com.brunogames.potionpop` · Política de privacidade: https://brunogames.com.br/jogos/potion-pop/privacidade

## Estrutura

```
Assets/_Game/
  Scripts/
    Core/         save, economia, vidas, progresso, prêmio diário, roleta, baú, missões, coleção, idiomas, áudio, vibração, arte
    Levels/       regras do tabuleiro (BoardState), gerador, solucionador e dificuldade — C# puro, testável
    UI/Framework/ design system em código: tokens (DS), tween, UIKit, partículas (FX), telas, popups
    UI/Screens/   Home, Loja, Ranking, Coleção, Perfil      UI/Chrome/  barra do topo e navegação
    UI/Popups/    popups de meta (Meta/) e comuns (Common/)
    Game/         tela da partida, sessão, HUD, reforços, tutorial, popups da fase; Board/ = tabuleiro e arraste
    Services/     Firebase (auth + Firestore), ranking, AdMob
  Resources/      Art/ (sprites + art_index.json), Audio/, Loc/*.csv (key,en,pt,es), catalog.json, ServicesConfig
  Plugins/        Android (Google Sign-In / Credential Manager), iOS (Google Sign-In, vibração)
  Editor/         builder do projeto, importadores, build, pós-processo iOS, debug, validador de fases, QA (QaTour)
  Tests/EditMode/ testes NUnit
Docs/             GDD.md, DesignSystem.md, Firebase-Setup.md, STATUS.md
Tools/            pipeline de arte (Codex), processamento, catálogo, áudio, checagem de compilação
ArtSource/raw/    imagens originais geradas pelo Codex
```

## Pipeline de conteúdo

```bash
python3 Tools/gen_art.py -j 10                                                   # gera no Codex as imagens que faltam
uv run --with pillow --with numpy --with scipy python Tools/process_art.py       # recorta/redimensiona → Resources/Art
uv run --with pillow --with numpy --with scipy python Tools/export_catalog.py    # catálogo + nomes dos produtos (3 idiomas)
uv run --with pillow --with numpy --with scipy python Tools/gen_audio.py         # sintetiza os 35 efeitos e as 3 músicas
Tools/compile_check.sh                                                           # compila o C# em ~1 s sem abrir o Unity
```

- Novos produtos/lojas: edite `Tools/catalog.py` e rode `gen_art.py`, `process_art.py` e `export_catalog.py`.
- Textos: um CSV por módulo em `Assets/_Game/Resources/Loc/` (colunas `key,en,pt,es`); use `Loc.T("chave")` no código.
