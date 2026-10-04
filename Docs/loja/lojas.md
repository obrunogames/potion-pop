# Lojas: apps, fichas, anúncios e builds

Estado em **04/10/2026, 13h**: **enviado para revisão nas duas lojas** (versão 1.0.0, build 1), com lançamento
**automático** depois da aprovação (Apple `AFTER_APPROVAL`; Play com a publicação gerenciada **desligada**). Mesmo
caminho do Shelf Pop! (`../shelf-sorting/Docs/loja/lojas.md`): mesmas contas, mesma chave da API, mesmos scripts
(`Tools/release/stores.mjs`, `promo_screenshots.mjs`, `data_safety.py`), adaptados ao Potion Pop!.

## Onde está cada coisa

| | App Store Connect | Google Play Console |
|---|---|---|
| Conta | Inside Tech LTDA (time `XT3CTXMM6Z`) | INSIDE TECH LTDA (conta `4941710782069944601`) |
| App | Apple ID **6819041216** (`ficha.json` → `appleId`), SKU `POTIONPOP-IOS`, bundle id do portal `7KBX435Y48` (Iniciar sessão com a Apple ligado) | app **4974651132167844467** |
| Id | bundle `br.com.brunogames.potionpop` | pacote `br.com.brunogames.potionpop` |
| Nome na loja | pt `Potion Pop! Separe as Cores` · en `Potion Pop! Color Sort Puzzle` · es `Potion Pop! Ordenar Colores` | igual |
| Idiomas da ficha | pt-BR (principal), en-US, es-MX | pt-BR (padrão), en-US, es-419 |
| Página pública (depois de publicar) | `https://apps.apple.com/app/id6819041216` | `https://play.google.com/store/apps/details?id=br.com.brunogames.potionpop` |

**Envio**: Play, 04/10 12:35 (pelo dono: 177 países + resto do mundo, lançamento completo); Apple, 04/10 12:59
("A aguardar revisão"). A Apple avisa que a revisão pode levar até 48 h.

**TestFlight**: grupo interno **Equipe Bruno Games** (acesso a todos os builds) com `thiagobrunomiranda@gmail.com`
(titular da conta). O build 1 já está em teste interno (sem revisão da Apple): o convite chega por e-mail e o app
aparece no TestFlight do iPhone.

**AdMob** (publisher `pub-9343332708932715`). Ids reais em `Assets/_Game/Resources/ServicesConfig.asset` e
`GoogleMobileAdsSettings.asset`; o build de release só aceita ids reais da plataforma que está gerando
(`BuildIdentityGuard`):

| | Android | iOS |
|---|---|---|
| App | `ca-app-pub-9343332708932715~4164007604` | `ca-app-pub-9343332708932715~9176746859` |
| Banner | `ca-app-pub-9343332708932715/3141821543` | `ca-app-pub-9343332708932715/5809095910` |
| Intersticial | `ca-app-pub-9343332708932715/1742867558` | `ca-app-pub-9343332708932715/9304739360` |
| Premiado | `ca-app-pub-9343332708932715/5142047359` | `ca-app-pub-9343332708932715/6652413458` |

**Firebase** `potion-pop-game`: login com Google e **com a Apple** ligados (Apple ligado no console em 04/10, sem
Services ID). SHA-1/SHA-256 da chave de upload **e** da assinatura do Play (`22:AE:9F:75:…`) cadastrados no app
Android (`firebase apps:android:sha:list 1:975002712260:android:d047147079532ed9beb191`).

**Site**: `/jogos/potion-pop`, privacidade `/jogos/potion-pop/privacidade` (en `/privacy`, es `/privacidad`),
suporte `/jogos/potion-pop/suporte`, exclusão de conta `/jogos/potion-pop/privacidade#excluir`.

## Arquivos desta pasta

| Arquivo | O que é |
|---|---|
| `ficha.json` | textos por idioma (nome, subtítulo, descrições, palavras-chave, novidades), categorias, classificação etária, contato e notas da revisão |
| `seguranca-dos-dados.csv` | Segurança dos dados do Play (`python3 Tools/release/data_safety.py <modelo.csv> …`), mesmas respostas do Shelf Pop |
| `privacidade-apple.json` | Privacidade do app na Apple (publicada em 04/10, 18 respostas, sem rastreamento) |
| `capturas/<pt-BR\|en-US\|es-419>/<iphone\|ipad\|android\|android-tablet>/` | 8 capturas por loja e idioma (`node Tools/release/promo_screenshots.mjs`, das capturas `Screenshots/qa/store_*` tiradas pelo QaTour). iPhone 1320×2868, iPad 2064×2752, Play celular **1080×1920** (o Play agora pede 9:16), tablets 1080×1920 |
| `imagens/` | ícone 512 e gráfico de destaque 1024×500 do Play |

As respostas dos formulários (classificação, público-alvo 13+ fora do Famílias, anúncios, ID de publicidade,
Segurança dos dados, Privacidade do app) são as mesmas do Shelf Pop: veja as tabelas em
`../shelf-sorting/Docs/loja/lojas.md`.

## O que falta (dono)

1. **Acompanhar a revisão** (e-mails da Apple e do Google). Os dois lançam sozinhos quando aprovados.
2. Quando aparecer na loja: AdMob › **Adicionar loja** nos dois apps (até lá a veiculação fica limitada); site:
   preencher `STORES.potionPop` em `src/config/site.ts` (links acima) e publicar.
3. **Revogação dos tokens da Apple** ao excluir a conta: o provedor Apple do Firebase está sem a seção "fluxo de
   código OAuth" (Services ID + chave *Sign in with Apple* do Apple Developer). A exclusão funciona (apaga nuvem,
   ranking e usuário), só não revoga o token da Apple. Para completar: `Docs/Firebase-Setup.md`, seção 7.1.
4. **Apple Developer Program vence em 14/10/2026**: confirme a renovação (vale para todos os jogos).
5. Próximas versões: suba `bundleVersion`, `AndroidBundleVersionCode` e `buildNumber`, rode
   `python3 Tools/release/build_release.py android|ios`, `node Tools/release/stores.mjs play-bundle <aab>` e
   `apple-build`, e envie.

## Scripts

```bash
python3 Tools/release/build_release.py android   # AAB assinado em Builds/release/
python3 Tools/release/build_release.py ios       # Xcode → archive → envio (conta da Apple logada no Xcode)
node Tools/release/promo_screenshots.mjs          # capturas das lojas + ícone/gráfico do Play
node Tools/release/stores.mjs check               # confere os limites da ficha (sem rede)
node Tools/release/stores.mjs play-bundle <aab>   # AAB como rascunho na produção (só permissões de versão)
node Tools/release/stores.mjs play-data-safety    # Segurança dos dados
node Tools/release/stores.mjs apple-draft         # textos, categorias, classificação, versão
REVIEW_CONTACT_FROM_APP=6818572338 node Tools/release/stores.mjs apple-extra   # grátis, países, contato, notas
node Tools/release/stores.mjs apple-screenshots   # capturas de iPhone e iPad
node Tools/release/stores.mjs apple-build [n]     # associa o build processado à versão
```

Credenciais (fora do git): `~/Desktop/games/CarrosRebaixados/Keystore` (chave da App Store Connect API e conta de
serviço do Play, `POTIONPOP_STORE_CREDENTIALS_DIR`) e `Keystore/` deste projeto (chave de upload Android).
