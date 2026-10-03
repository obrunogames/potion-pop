# Potion Pop! — Configuração do Firebase, logins e AdMob

Guia passo a passo para ligar a nuvem (login Google/Apple + salvamento no Firestore + ranking global) e os anúncios
reais (AdMob + consentimento UMP). **Sem nada disso o jogo funciona 100% offline**: com o `firebaseApiKey` vazio o
login mostra "nuvem desativada" e os anúncios usam os IDs de teste do Google.

> Arquitetura: o jogo **não usa o SDK Firebase para Unity**. O módulo Services fala direto com as APIs REST
> (`identitytoolkit`, `securetoken`, `firestore.googleapis.com`) usando os ID tokens nativos do Google
> (Credential Manager no Android, GoogleSignIn no iOS) e da Apple (pacote AppleAuth). Por isso **não é preciso**
> colocar `google-services.json` nem `GoogleService-Info.plist` no projeto — só preencher o
> `Resources/ServicesConfig.asset`.

---

## 1. Criar o projeto Firebase

1. Acesse <https://console.firebase.google.com> → **Adicionar projeto** → nome `Potion Pop` (o ID fica algo como
   `potion-pop-1a2b3`). O Google Analytics é opcional (o jogo não usa).
2. Em **Configurações do projeto → Geral**, anote:
   * **ID do projeto** → vai em `firebaseProjectId`;
   * **Chave de API da Web** (Web API Key) → vai em `firebaseApiKey`.
     (Ela só aparece depois que algum serviço de Authentication é ativado — faça o passo 3 e volte aqui.)
3. Ainda em **Geral → Seus apps**, registre os dois apps (isso cria os clientes OAuth que o login do Google usa):
   * **Android**: nome do pacote = `Player Settings → Other Settings → Package Name` (deve ser `br.com.brunogames.potionpop`, o `PotionPopBuilder.BundleId`; se o Player Settings ainda mostrar outro valor, rode antes **Potion Pop ▸ Rebuild Project**).
   * **iOS**: ID do pacote = `Bundle Identifier` do Player Settings (normalmente o mesmo).
   * Pode pular o download dos arquivos de configuração e os passos de SDK (não usamos), mas **guarde o
     `GoogleService-Info.plist`** do passo 5.

## 2. Ativar o login com Google

1. **Authentication → Começar → Método de login → Google → Ativar**. Escolha o e-mail de suporte e salve.
2. Abra de novo o provedor Google e expanda **Configuração do SDK da Web**: copie o **ID do cliente da Web**
   (termina em `.apps.googleusercontent.com`) → vai em `googleWebClientId`.
   * É esse cliente "Web application" que o Android usa como `serverClientId` e que o Firebase aceita no
     `accounts:signInWithIdp` (o ID token precisa ter esse *audience*).

## 3. Android: impressões digitais SHA-1

O Credential Manager só devolve o token se existir um cliente OAuth Android com **pacote + SHA-1** do certificado que
assinou o APK/AAB.

1. Gere o SHA-1 de cada certificado usado:
   ```bash
   # keystore de upload/release (o mesmo configurado em Player Settings → Publishing Settings)
   keytool -list -v -keystore caminho/para/user.keystore -alias SEU_ALIAS
   # keystore de debug do Unity (builds de desenvolvimento)
   keytool -list -v -keystore ~/.android/debug.keystore -alias androiddebugkey -storepass android -keypass android
   ```
2. Se publicar com **Assinatura de apps do Google Play**, copie também o SHA-1 em
   *Play Console → Configuração → Integridade do app → Assinatura de apps* (é o certificado que vai para os aparelhos).
3. No Firebase: **Configurações do projeto → Seus apps → Android → Adicionar impressão digital**; adicione **todos**
   os SHA-1 (debug, upload e Play). O Firebase cria/atualiza o cliente OAuth Android automaticamente.
4. Requisitos de build Android (o builder do projeto já deve deixar assim; confira):
   * *Player Settings → Publishing Settings*: **Custom Main Gradle Template** e **Custom Gradle Properties
     Template** marcados;
   * *Assets → External Dependency Manager → Android Resolver → Force Resolve* (puxa
     `androidx.credentials:credentials`, `credentials-play-services-auth` e `googleid`, declarados em
     `Assets/_Game/Editor/PotionPopDependencies.xml`);
   * se usar *Minify* (R8), a classe `com.potionpop.auth.GoogleSignInBridge` já está anotada com `@Keep`.

## 4. iOS: ID do cliente e URL scheme

1. Abra o `GoogleService-Info.plist` baixado no passo 1 (se ele não tiver `CLIENT_ID`, baixe de novo **depois** de
   ativar o provedor Google) ou vá em *Google Cloud Console → APIs e serviços → Credenciais → "iOS client for …"*.
2. `CLIENT_ID` → vai em `googleIosClientId`.
3. `REVERSED_CLIENT_ID` (`com.googleusercontent.apps.XXXX`) precisa estar registrado como **URL Type** no
   `Info.plist`. O pós-processamento iOS do projeto deve adicioná-lo a partir do `googleIosClientId`; se não
   adicionar, faça no Xcode: *Target Unity-iPhone → Info → URL Types → +* com esse valor em *URL Schemes*.
   Sem isso o GoogleSignIn aborta o login (o jogo mostra "opção de login indisponível").
4. O pod `GoogleSignIn ~> 8.0` entra pelo EDM4U (mesmo arquivo de dependências). Abra sempre o
   **`Unity-iPhone.xcworkspace`** (não o `.xcodeproj`).

## 5. Login com a Apple (iOS)

1. **Apple Developer → Certificates, IDs & Profiles → Identifiers**: no App ID do jogo, marque
   **Sign in with Apple**. No Xcode o target precisa da capability *Sign in with Apple* (o pós-processamento iOS
   adiciona via AppleAuth; confira em *Signing & Capabilities*).
2. **Firebase → Authentication → Método de login → Apple → Ativar.** Para o login nativo no iOS basta isso.
3. **Revogação de tokens ao excluir a conta** (exigência da Apple, diretriz 5.1.1(v)) — preencha também a seção
   *Configuração do fluxo de código OAuth* do provedor Apple no Firebase:
   * **Services ID**: crie em *Identifiers → Services IDs* (ex.: `com.suaempresa.potionpop.signin`), ative
     *Sign in with Apple* e configure o domínio `SEU-PROJETO.firebaseapp.com` e a URL de retorno
     `https://SEU-PROJETO.firebaseapp.com/__/auth/handler`;
   * **Apple Team ID**;
   * **Key ID + chave privada**: crie em *Keys → +* com *Sign in with Apple* marcado e baixe o `.p8`.
   O jogo pede um novo login da Apple ao excluir a conta e chama `accounts:revokeToken`; se essa seção não estiver
   preenchida a exclusão continua funcionando, só a revogação falha (aparece um aviso no log).
4. A Apple só envia o nome do jogador **no primeiro login**; o jogo grava esse nome no usuário do Firebase.

## 6. Firestore

1. **Firestore Database → Criar banco de dados** → modo **produção** → região perto dos jogadores
   (ex.: `southamerica-east1` para o Brasil ou `nam5` para EUA). A região não pode ser trocada depois.
2. Regras: copie o conteúdo de [`firebase/firestore.rules`](../firebase/firestore.rules) para
   *Firestore → Regras* e clique em **Publicar**. Pela linha de comando:
   ```bash
   npm install -g firebase-tools
   firebase login
   cd firebase && firebase init firestore   # escolha o projeto; mantenha firestore.rules
   firebase deploy --only firestore:rules
   ```
   As regras deixam o **save** (`players/{uid}`) visível **só para o próprio dono**. O ranking global lê a coleção
   pública `leaderboard/{uid}` (nome, avatar, estrelas, fase), legível por qualquer jogador logado. Só o dono
   **grava/exclui** os próprios documentos, com validação de tipos e tamanhos.
3. Índices: nenhum índice composto é necessário (o ranking usa `orderBy stars desc limit 50` e uma contagem
   `stars > N`, cobertos pelos índices automáticos de campo único).
4. Documento salvo em `players/{uid}`:

   | Campo | Tipo | Conteúdo |
   |---|---|---|
   | `save` | string | `PlayerData` inteiro em JSON |
   | `level` | integer | próximo nível |
   | `stars` | integer | estrelas totais (ranking global) |
   | `name` | string | nome do jogador (até 24 caracteres) |
   | `avatar` | string | id do avatar (`puppy`, `fox`, …) |
   | `updatedAt` | integer | unix seconds do último salvamento local |

   E a linha pública do ranking em `leaderboard/{uid}` (gravada logo depois do save): `name`, `avatar`, `stars`, `level`,
   `updatedAt` — sem o save. Excluir a conta apaga os dois documentos.

## 7. Restringir a chave de API (recomendado)

*Google Cloud Console → APIs e serviços → Credenciais → Browser key (auto created by Firebase)* (é a "Web API Key"):

* **Restrições de API** → *Restringir chave* → marque **Identity Toolkit API**, **Token Service API** e
  **Cloud Firestore API**.
* **Restrições de aplicativo** → deixe **Nenhuma**. (Uma mesma chave não pode ser restrita a Android e iOS ao mesmo
  tempo; o jogo envia `X-Android-Package`/`X-Android-Cert` e `X-Ios-Bundle-Identifier`, então você *pode* usar
  chaves separadas restritas por app, mas o `ServicesConfig` tem um campo só.)

## 8. Preencher o `ServicesConfig`

`Assets/_Game/Resources/ServicesConfig.asset` (criado pelo builder do editor; se não existir:
*Create → ScriptableObject* do tipo `ServicesConfig` com esse nome exato dentro de uma pasta `Resources`).

| Campo | Onde pegar |
|---|---|
| `firebaseApiKey` | passo 1 (Web API Key) |
| `firebaseProjectId` | passo 1 (ID do projeto) |
| `googleWebClientId` | passo 2 (ID do cliente da Web) |
| `googleIosClientId` | passo 4 (`CLIENT_ID`) |
| `admobAndroidAppId` / `admobIosAppId` | passo 9 |
| `banner*`, `interstitial*`, `rewarded*` | passo 9 (um ID por formato e plataforma) |
| `simulatedAdsInEditor` | marque para testar o fluxo de anúncios no Editor sem rede (overlay "Anúncio (teste)") |
| `admobTestDeviceIds` | IDs de aparelhos de teste (o SDK imprime no logcat/Xcode) |
| `umpDebugGeographyEea` + `umpTestDeviceHashedIds` | força o formulário GDPR em aparelhos de teste |
| `privacyPolicyUrl`, `termsUrl`, `supportEmail` | links mostrados nas Configurações |

> ⚠️ Os IDs padrão do AdMob são os **IDs de teste oficiais do Google**. Troque antes de publicar — e nunca clique
> nos seus próprios anúncios reais.

## 9. AdMob

1. <https://admob.google.com> → **Apps → Adicionar app** (um para Android, outro para iOS; se ainda não publicou,
   escolha "não está listado"). Copie o **ID do app** (`ca-app-pub-…~…`) → `admobAndroidAppId` / `admobIosAppId`.
   O plugin do Google também lê o ID em *Assets → Google Mobile Ads → Settings*: o builder copia do
   `ServicesConfig`; se não copiar, preencha lá também (sem isso o app fecha ao abrir no aparelho).
2. Em cada app, crie os **blocos de anúncios**:
   * **Banner** (o jogo usa banner adaptativo ancorado embaixo, só durante a partida);
   * **Intersticial** (ao fim de um nível a partir do nível 6, no máximo a cada 2 níveis, ≥ 90 s de intervalo e
     nunca logo depois de um vídeo premiado);
   * **Premiado** (continuar +60 s, moedas em dobro, +1 coração, giro extra, moedas da loja, desbloquear
     prateleira).
   Copie cada ID (`ca-app-pub-…/…`) para o campo correspondente.
3. Publique o arquivo **app-ads.txt** no site do desenvolvedor informado nas lojas (*AdMob → Apps → app-ads.txt*).
4. Classificação de conteúdo (*Bloqueio de conteúdo*): recomendo **G/PG** para combinar com o público casual.

## 10. Mensagens de privacidade (UMP / GDPR / ATT)

O jogo chama o UMP **antes** de inicializar os anúncios (`ConsentInformation.Update` →
`ConsentForm.LoadAndShowConsentFormIfRequired`) e só depois `MobileAds.Initialize`.

1. *AdMob → Privacidade e mensagens → Regulamentações europeias (GDPR)* → **Criar mensagem**: selecione os dois
   apps, idiomas **inglês, português e espanhol**, informe a URL da política de privacidade e **Publique**.
2. *Regulamentações estaduais dos EUA*: crie a mensagem também (recomendado).
3. iOS — **não** publique a *Mensagem explicativa do IDFA*: o jogo não pede rastreamento (sem pedido de ATT da
   Apple; os anúncios no iOS são servidos sem o IDFA) e o build não declara `NSUserTrackingUsageDescription` (o
   pós-processamento iOS remove a chave do `Info.plist`). Se um dia quiser pedir o ATT, é preciso voltar a declarar o
   texto e mudar as respostas de privacidade da App Store para "usado para rastrear".
4. Nas **Configurações** do jogo, mostre o botão "Opções de privacidade" quando
   `AdsService.PrivacyOptionsRequired` for `true` e chame `AdsService.ShowPrivacyOptions()`.

## 11. Testes

**No Editor (sem configurar nada):**
* Login: Google e Apple usam o *mock* — após ~1 s entra o usuário **"Mimi Fan"** (uid `mock-…`, fixo por máquina).
* Nuvem: o "Firestore" do mock fica em `Application.persistentDataPath/mock_cloud.json`
  (macOS: `~/Library/Application Support/<empresa>/<produto>/`). Apague o arquivo para "zerar a nuvem"; edite o
  nível dentro do JSON para testar a restauração (vence o save com **nível maior**, depois mais estrelas, depois o
  mais recente).
* Anúncios: por padrão o plugin do Google mostra anúncios *placeholder* no Editor. Com `simulatedAdsInEditor`
  marcado aparece o overlay **"Anúncio (teste)"** com contagem de 3 s (no premiado, "Pular" = sem recompensa) e um
  banner falso embaixo.

**No aparelho:**
* Android: `adb logcat -s Unity SPGoogleSignIn` e procure as tags `[Auth]`, `[CloudSave]`, `[Ads]`,
  `[Leaderboard]`.
* iOS: console do Xcode, mesmas tags.
* Para ver o formulário GDPR fora da Europa: rode uma vez, copie do log o *hashed device id* do UMP para
  `umpTestDeviceHashedIds` e marque `umpDebugGeographyEea`. Para "resetar" o consentimento, desinstale o app.
* Confira os documentos em *Firestore → Dados → players* (privado) e *leaderboard* (ranking público).
* Excluir conta: *Configurações → Conta → Excluir conta* deve apagar o documento e o usuário em
  *Authentication → Usuários* (o login pode ser pedido de novo — o Firebase exige login recente).

**Erros comuns**

| Sintoma / log | Causa provável |
|---|---|
| Android `[28444] Developer console is not set up correctly` / `nocredential` | SHA-1 ou nome do pacote não cadastrado no app Android do Firebase (passo 3), ou `googleWebClientId` errado |
| `INVALID_IDP_RESPONSE` no `signInWithIdp` | o token foi emitido para outro cliente (use o ID do cliente **Web** do mesmo projeto) |
| `OPERATION_NOT_ALLOWED` | provedor Google/Apple desativado no Authentication |
| `API key not valid` / `PERMISSION_DENIED … API has not been used` | chave errada ou API não incluída na restrição (passo 7) |
| `[CloudSave] … HTTP 403` | regras do Firestore não publicadas ou documento fora do formato |
| iOS: "missing support for the following URL schemes" | `REVERSED_CLIENT_ID` ausente nos URL Types (passo 4) |
| Anúncios nunca carregam | IDs reais em app novo (aguarde aprovação/até 1 h), sem consentimento UMP ainda, ou aparelho sem internet — o jogo tenta de novo com espera crescente |

## 12. Checklist de publicação

- [ ] `ServicesConfig` com Firebase, clientes OAuth e **IDs reais** do AdMob.
- [ ] Google Mobile Ads Settings com os App IDs reais.
- [ ] SHA-1 de upload **e** da Assinatura de apps do Play cadastrados.
- [ ] `REVERSED_CLIENT_ID` no Info.plist, capability *Sign in with Apple*, **sem** `NSUserTrackingUsageDescription`.
- [ ] Regras do Firestore publicadas.
- [ ] Mensagens GDPR / estados dos EUA publicadas no AdMob (a de IDFA **não**).
- [ ] Provedor Apple com Services ID + chave (revogação na exclusão de conta).
- [ ] `app-ads.txt` no ar; política de privacidade cita Firebase (Auth/Firestore) e AdMob.
- [ ] Formulários de privacidade das lojas (Data safety / App Privacy): identificadores do usuário (uid), e-mail
      (login), dados de jogo, identificadores de publicidade.
