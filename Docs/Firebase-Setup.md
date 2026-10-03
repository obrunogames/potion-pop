# Potion Pop! — Firebase, logins e AdMob

O que já está configurado na nuvem (feito pela **Firebase CLI**, não pelo console), os valores que o jogo usa e o que
ainda falta fazer à mão. **Sem a nuvem o jogo continua 100% offline**: com o `firebaseApiKey` vazio o login mostra
"nuvem desativada" e os anúncios usam os IDs de teste do Google.

> Arquitetura: o jogo **não usa o SDK Firebase para Unity**. O módulo Services fala direto com as APIs REST
> (`identitytoolkit`, `securetoken`, `firestore.googleapis.com`) usando os ID tokens nativos do Google
> (Credential Manager no Android, GoogleSignIn no iOS) e da Apple (pacote AppleAuth). Por isso **não é preciso**
> colocar `google-services.json` nem `GoogleService-Info.plist` no projeto: basta o
> `Assets/_Game/Resources/ServicesConfig.asset` (já preenchido, seção 4).

---

## 1. Resumo do estado (configurado em 03/10/2026)

| Item | Estado |
|---|---|
| Projeto Firebase `potion-pop-game` | **Feito** (CLI) |
| Firestore `(default)` em `southamerica-east1` + regras + índices | **Feito** (CLI, `firebase deploy`) |
| Login com Google | **Feito** (CLI, bloco `auth` do `firebase.json`) |
| Login anônimo / e-mail e senha | Desativados **de propósito** (o jogo não usa) |
| Apps Android e iOS registrados | **Feito** (CLI) |
| Chave de upload Android + SHA-1/SHA-256 (upload e debug) no app Android | **Feito** (keytool + CLI) |
| `ServicesConfig`: Firebase e clientes OAuth do Google | **Feito** |
| Verificação REST (Auth + regras do Firestore) | **Feito** (seção 6) |
| Login com a Apple (provedor + Services ID/chave para revogação) | **Pendente — manual** (7.1) |
| SHA-1/SHA-256 da Assinatura de apps do Google Play | **Pendente** — depois que o app existir no Play Console (7.2) |
| AdMob: apps e blocos de anúncios reais | **Pendente — manual** (7.3); hoje o `ServicesConfig` tem os IDs de teste do Google |
| Restringir a chave de API | **Pendente — recomendado** (7.4) |
| Conferir a tela de consentimento OAuth (público externo, em produção) | **Conferir** (7.5) |

## 2. Identificadores

| Item | Valor |
|---|---|
| ID do projeto | `potion-pop-game` |
| Nome do projeto | Potion Pop |
| Número do projeto | `975002712260` |
| Console | <https://console.firebase.google.com/project/potion-pop-game/overview> |
| Conta dona | `thiagobrunomiranda@gmail.com` (a mesma da Firebase CLI) |
| Plano | Spark (sem faturamento) — ver nota no fim desta seção |
| App Android | `1:975002712260:android:d047147079532ed9beb191` — pacote `br.com.brunogames.potionpop`, nome "Potion Pop!" |
| App iOS | `1:975002712260:ios:a92ba1ba1caad18ebeb191` — bundle id `br.com.brunogames.potionpop`, nome "Potion Pop!" |
| App Web | `1:975002712260:web:b3eb7adf1551bd11beb191` — "Default Web App", criado sozinho pelo `deploy --only auth` (é a origem da Web API Key) |
| Firestore | banco `(default)`, modo nativo, edição Standard, `southamerica-east1` (não pode ser trocada) |
| Auth domain | `potion-pop-game.firebaseapp.com` |
| Modo do Authentication | Firebase Authentication with Identity Platform (`subtype IDENTITY_PLATFORM`) |

**Clientes OAuth** (criados automaticamente quando o login com Google foi ligado e os apps/SHA-1 foram registrados):

| Cliente | ID | Uso |
|---|---|---|
| Web (tipo 3) | `975002712260-fn44ai5c45g2u42l1n7kbohk3soaapu8.apps.googleusercontent.com` | `googleWebClientId`: `serverClientId` no Android, `GIDServerClientID` no iOS; é o `clientId` do provedor Google no Firebase (o *audience* aceito pelo `accounts:signInWithIdp`) |
| iOS (tipo 2) | `975002712260-50t6snes3rosvrqf6lj6csb1pnupfr3j.apps.googleusercontent.com` | `googleIosClientId` (`GIDClientID`); `REVERSED_CLIENT_ID` = `com.googleusercontent.apps.975002712260-50t6snes3rosvrqf6lj6csb1pnupfr3j` (URL scheme, adicionado pelo pós-processamento iOS) |
| Android (tipo 1) — upload | `975002712260-mqcakblf5h27nlo6ed9q0nm0stht1evo.apps.googleusercontent.com` | pacote + SHA-1 da chave de upload (o jogo não referencia; o Credential Manager confere) |
| Android (tipo 1) — debug | `975002712260-tkducb3tj1nn4rj5ko0hq3cnia1b5q2q.apps.googleusercontent.com` | pacote + SHA-1 do `~/.android/debug.keystore` deste Mac |

**Certificados cadastrados no app Android** (`firebase apps:android:sha:list 1:975002712260:android:d047147079532ed9beb191`):

| Certificado | SHA-1 | SHA-256 |
|---|---|---|
| Upload — `Keystore/PotionPop-upload.keystore`, alias `potionpop` | `37:8B:3A:7A:87:3E:8E:C7:C4:43:7A:C9:8F:83:65:D2:0A:93:82:42` | `73:EE:7E:DD:13:15:B1:72:4D:1A:AD:61:2A:AE:F6:FA:26:EE:08:69:20:1C:0F:B1:0B:B8:63:87:8C:D3:A3:96` |
| Debug — `~/.android/debug.keystore` deste Mac (builds de desenvolvimento do Unity) | `F2:BF:74:6D:95:47:66:B9:1C:A7:34:C4:BA:F7:30:EE:9E:2B:2F:C0` | `34:32:0E:5A:96:75:FB:90:52:C9:B7:D4:9F:CB:14:B1:47:8A:14:E3:A0:01:29:A9:2C:FF:D0:A6:69:DF:1C:7A` |
| Assinatura de apps do Google Play | *pendente (7.2)* | *pendente (7.2)* |

> **Plano Spark.** Cotas gratuitas do Firestore: 50 mil leituras, 20 mil gravações e 20 mil exclusões por dia e
> 1 GiB armazenado. Com o Authentication no modo Identity Platform, o Spark também limita os usuários ativos por dia
> (3.000 DAU quando isto foi configurado). Se o jogo crescer, ative o plano Blaze (continua sem custo dentro das cotas).
> Confira os valores atuais em <https://firebase.google.com/pricing>.

## 3. O que foi feito pela CLI (reproduzível)

Arquivos em [`firebase/`](../firebase): `.firebaserc` (projeto padrão `potion-pop-game`), `firebase.json` (banco,
região, regras, índices e o bloco `auth`), `firestore.rules`, `firestore.indexes.json`. Rode tudo **de dentro da
pasta `firebase/`**, logado com `firebase login` na conta dona.

```bash
cd firebase
firebase projects:create potion-pop-game --display-name "Potion Pop"
# .firebaserc → {"projects": {"default": "potion-pop-game"}}

# Ativa a API do Firestore, cria o banco (default) na região do firebase.json (southamerica-east1),
# compila e publica as regras e os índices:
firebase deploy --only firestore
# + bloco "auth" do firebase.json: liga o provedor Google (marca OAuth "Potion Pop!", e-mail de suporte
#   thiagobrunomiranda@gmail.com) e cria o "Default Web App" usado no provisionamento:
firebase deploy --only firestore,auth

firebase apps:create android "Potion Pop!" --package-name br.com.brunogames.potionpop
firebase apps:create ios "Potion Pop!" --bundle-id br.com.brunogames.potionpop

# Impressões digitais (hex sem dois-pontos; o tipo SHA-1/SHA-256 é deduzido pelo tamanho):
APP=1:975002712260:android:d047147079532ed9beb191
firebase apps:android:sha:create $APP 378b3a7a873e8ec7c4437ac98f8365d20a938242                                  # upload SHA-1
firebase apps:android:sha:create $APP 73ee7edd1315b1724d1aad612aaef6fa26ee0869201c0fb10bb863878cd3a396          # upload SHA-256
firebase apps:android:sha:create $APP f2bf746d954766b91ca734c4baf730ee9e2b2fc0                                  # debug SHA-1
firebase apps:android:sha:create $APP 34320e5a9675fb9052c9b7d49fcb14b1478a14e3a00129a92cffd0a669df1c7a          # debug SHA-256

# Configurações (só para ler os IDs; não vão para o projeto Unity):
firebase apps:sdkconfig android $APP                                         # google-services.json: oauth_client tipo 3 = cliente Web
firebase apps:sdkconfig ios 1:975002712260:ios:a92ba1ba1caad18ebeb191        # GoogleService-Info.plist: CLIENT_ID / REVERSED_CLIENT_ID
firebase apps:sdkconfig web 1:975002712260:web:b3eb7adf1551bd11beb191        # apiKey = Web API Key
```

* Mudou as regras? `firebase deploy --only firestore:rules` (ou `firebase deploy --only firestore,auth`, que é
  idempotente).
* O banco foi criado **sem** proteção contra exclusão. Para ligar (recomendado depois do lançamento):
  `firebase firestore:databases:update "(default)" --delete-protection ENABLED`.
* O `gcloud` desta máquina está logado em outra conta (`thiago.bruno@begrowth.com.br`), sem acesso ao projeto: use a
  Firebase CLI ou o console com a conta dona.

## 4. `ServicesConfig` (preenchido)

`Assets/_Game/Resources/ServicesConfig.asset` (o builder do editor só cria o asset quando ele não existe; não
sobrescreve estes valores):

| Campo | Valor | Origem |
|---|---|---|
| `firebaseApiKey` | `AIzaSyD-oy184WTZrWbZqCxJqcrfMYaICskMY3w` | Web API Key ("Browser key (auto created by Firebase)"), `apps:sdkconfig web` |
| `firebaseProjectId` | `potion-pop-game` | |
| `googleWebClientId` | `975002712260-fn44ai5c45g2u42l1n7kbohk3soaapu8.apps.googleusercontent.com` | `oauth_client` tipo 3 do `google-services.json` (= `clientId` do provedor Google) |
| `googleIosClientId` | `975002712260-50t6snes3rosvrqf6lj6csb1pnupfr3j.apps.googleusercontent.com` | `CLIENT_ID` do `GoogleService-Info.plist` |
| `admobAndroidAppId`, `admobIosAppId`, `banner*`, `interstitial*`, `rewarded*` | IDs de **teste** do Google | pendente (7.3) |
| `simulatedAdsInEditor`, `admobTestDeviceIds`, `umpDebugGeographyEea`, `umpTestDeviceHashedIds` | inalterados | testes (seção 9) |
| `privacyPolicyUrl`, `termsUrl`, `supportEmail` | inalterados | links das Configurações |

* A chave de API e os IDs de cliente **não são segredo** (vão dentro do app). **Nunca** coloque senhas de keystore
  aqui nem em nenhum arquivo versionado.
* Por que a Web API Key e não as chaves de plataforma: o `ServicesConfig` tem um campo só para Android e iOS. A Browser
  key não tem restrição de aplicativo (só de APIs), então funciona nas duas plataformas. O Firebase também criou
  "Android key" e "iOS key (auto created by Firebase)" (estão no `google-services.json` / `GoogleService-Info.plist`);
  o jogo não as usa — não precisa mexer nelas.
* As chamadas ao Firestore não levam a chave: vão só com `Authorization: Bearer <ID token do Firebase>`. A chave é
  usada no `identitytoolkit` (login, perfil, exclusão, revogação) e no `securetoken` (renovar a sessão).
* Builds **de release** (não-development) para Android/iOS **falham** no `BuildIdentityGuard` enquanto houver ID de
  teste do AdMob no `ServicesConfig`. Builds de desenvolvimento funcionam normalmente.

## 5. Assinatura Android (chave de upload)

| Arquivo | Conteúdo |
|---|---|
| `Keystore/PotionPop-upload.keystore` | PKCS12, RSA 2048, SHA256withRSA, alias `potionpop`, `CN=Potion Pop, O=Inside Tech LTDA, L=Brasil, C=BR`, válida de 03/10/2026 a 18/02/2054 (10.000 dias) |
| `Keystore/release-signing.json` | `{"keystore": "Keystore/PotionPop-upload.keystore", "keystorePass": "…", "alias": "potionpop", "keyPass": "…"}` — mesmo formato do Shelf Pop; senhas aleatórias de 40 caracteres; `chmod 600` |

* A pasta `Keystore/` está no `.gitignore` (nunca vai para o git). **Guarde os dois arquivos no cofre/backup**: sem a
  chave de upload só dá para atualizar o app pedindo a troca da chave ao suporte do Google Play.
* Em PKCS12 a senha da chave é obrigatoriamente a mesma da keystore (`keyPass` = `keystorePass`).
* O keytool usado foi o do Unity
  (`/Applications/Unity/Hub/Editor/6000.6.3f1/PlaybackEngines/AndroidPlayer/OpenJDK/bin/keytool`); o `/usr/bin/keytool`
  do macOS precisa de um Java instalado.
* O build lê a assinatura de variáveis de ambiente (`BuildScript.cs`): `POTIONPOP_KEYSTORE` (caminho),
  `POTIONPOP_KEYSTORE_PASS`, `POTIONPOP_KEY_ALIAS`, `POTIONPOP_KEY_PASS`. Para exportar a partir do JSON sem mostrar
  as senhas (com o Unity fechado, na raiz do projeto):
  ```bash
  export POTIONPOP_KEYSTORE="$PWD/Keystore/PotionPop-upload.keystore"
  export POTIONPOP_KEY_ALIAS=potionpop
  export POTIONPOP_KEYSTORE_PASS="$(python3 -c 'import json;print(json.load(open("Keystore/release-signing.json"))["keystorePass"])')"
  export POTIONPOP_KEY_PASS="$(python3 -c 'import json;print(json.load(open("Keystore/release-signing.json"))["keyPass"])')"
  /Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/MacOS/Unity -batchmode -quit -projectPath . \
    -buildTarget Android -executeMethod PotionPop.EditorTools.BuildScript.BuildAndroidAab
  ```
  (No Shelf Pop, `Tools/release/build_release.py` faz isso lendo o mesmo formato de JSON.)
* Para ver as impressões digitais de novo (só dados públicos do certificado):
  ```bash
  KT=/Applications/Unity/Hub/Editor/6000.6.3f1/PlaybackEngines/AndroidPlayer/OpenJDK/bin/keytool
  export PP_PASS="$(python3 -c 'import json;print(json.load(open("Keystore/release-signing.json"))["keystorePass"])')"
  "$KT" -list -v -keystore Keystore/PotionPop-upload.keystore -alias potionpop -storepass:env PP_PASS | grep -E "SHA1|SHA256"
  unset PP_PASS
  ```

## 6. Verificação feita (03/10/2026)

Chamadas REST com a Web API Key, no formato que o jogo usa:

| Teste | Resultado | Conclusão |
|---|---|---|
| `accounts:signInWithIdp` com `providerId=google.com` e ID token falso — sem cabeçalhos, com `X-Android-Package`/`X-Android-Cert` e com `X-Ios-Bundle-Identifier` | HTTP 400 `INVALID_IDP_RESPONSE` (token ilegível) nos três casos | chave + projeto certos, provedor Google **ligado**, chave sem restrição de app |
| `accounts:signInWithIdp` com `providerId=apple.com` | HTTP 400 `OPERATION_NOT_ALLOWED` | provedor Apple ainda desligado (7.1) |
| `accounts:signUp` anônimo | HTTP 400 `ADMIN_ONLY_OPERATION` | anônimo desligado (nenhum usuário criado) |
| `securetoken …/token` com refresh token falso | HTTP 400 `INVALID_REFRESH_TOKEN` | Token Service acessível com a chave |
| Controle: chave inválida | HTTP 400 `API key not valid` | |
| Firestore sem login: `PATCH players/test` (documento no formato válido), `GET players/test`, `PATCH leaderboard/test`, `runQuery` do ranking, `POST reports`, `DELETE players/test` | HTTP 403 `PERMISSION_DENIED` em todos | regras publicadas e fechadas para quem não está logado |
| Firestore com token forjado | HTTP 401 `UNAUTHENTICATED` | |
| API de teste de regras (`firebaserules … :test`, auth simulado, nada gravado): dono cria/lê o próprio save e a própria linha do ranking; outro usuário não lê nem grava o save alheio; campo extra é recusado; logado lista o ranking e anônimo não; denúncia só em nome próprio e ninguém lê denúncias; coleção desconhecida negada | 12/12 conforme o esperado | lógica das regras confirmada |
| Regras publicadas × `firebase/firestore.rules` | idênticas | |
| Configuração do Auth (API admin) | Google ligado com `clientId` = cliente Web acima; anônimo, e-mail e telefone desligados | |
| Dados deixados para trás | 0 usuários no Auth, 0 coleções no Firestore | |

Para repetir as verificações principais:
```bash
KEY=AIzaSyD-oy184WTZrWbZqCxJqcrfMYaICskMY3w
# Google ligado → INVALID_IDP_RESPONSE (token falso). Desligado → OPERATION_NOT_ALLOWED.
curl -s -H 'Content-Type: application/json' \
  -d '{"postBody":"id_token=x&providerId=google.com","requestUri":"http://localhost","returnSecureToken":true}' \
  "https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp?key=$KEY"
# Depois de ligar a Apple (7.1), troque google.com por apple.com: deve sair de OPERATION_NOT_ALLOWED para INVALID_IDP_RESPONSE.
# Gravação sem login → 403 PERMISSION_DENIED.
curl -s -X PATCH -H 'Content-Type: application/json' -d '{"fields":{}}' \
  "https://firestore.googleapis.com/v1/projects/potion-pop-game/databases/(default)/documents/players/test"
```

## 7. Pendências manuais

### 7.1 Login com a Apple (Apple Developer + console do Firebase)

1. **Apple Developer → Certificates, IDs & Profiles → Identifiers**: no App ID `br.com.brunogames.potionpop`, marque
   **Sign in with Apple**. No Xcode o target precisa da capability *Sign in with Apple* (o pós-processamento iOS
   adiciona via AppleAuth; confira em *Signing & Capabilities*).
2. **Firebase → Authentication → Método de login → Adicionar provedor → Apple → Ativar.** Para o login nativo no iOS
   basta isso (o *audience* do token da Apple é o bundle id).
3. **Revogação de tokens ao excluir a conta** (exigência da Apple, diretriz 5.1.1(v)) — preencha também a seção
   *Configuração do fluxo de código OAuth* do provedor Apple no Firebase:
   * **Services ID**: crie em *Identifiers → Services IDs* (sugestão: `br.com.brunogames.potionpop.signin`), ative
     *Sign in with Apple*, domínio `potion-pop-game.firebaseapp.com` e URL de retorno
     `https://potion-pop-game.firebaseapp.com/__/auth/handler`;
   * **Apple Team ID**;
   * **Key ID + chave privada**: crie em *Keys → +* com *Sign in with Apple* marcado e baixe o `.p8` (guarde no
     cofre ou em `Keystore/`, que não vai para o git).
   O jogo pede um novo login da Apple ao excluir a conta e chama `accounts:revokeToken`; sem essa seção a exclusão
   continua funcionando, só a revogação falha (aparece um aviso no log).
4. A Apple só envia o nome do jogador **no primeiro login**; o jogo grava esse nome no usuário do Firebase.
5. Confirme com o `curl` da seção 6 trocando `google.com` por `apple.com`.

### 7.2 SHA-1/SHA-256 da Assinatura de apps do Google Play

Com a Assinatura de apps do Play (padrão), o certificado que chega aos aparelhos é o do Google, não o de upload. Sem ele
o login com Google falha nas instalações vindas da Play Store (`[28444] Developer console is not set up correctly`).

1. Depois de criar o app no Play Console e enviar o primeiro AAB: *Play Console → (app) → Testar e lançar →
   Configuração → Integridade do app → Assinatura de apps* → copie o **SHA-1** e o **SHA-256** do
   *certificado da chave de assinatura do app*.
2. Cadastre (hex sem dois-pontos):
   ```bash
   cd firebase
   firebase apps:android:sha:create 1:975002712260:android:d047147079532ed9beb191 <SHA-1>
   firebase apps:android:sha:create 1:975002712260:android:d047147079532ed9beb191 <SHA-256>
   firebase apps:android:sha:list 1:975002712260:android:d047147079532ed9beb191
   ```
   O cliente OAuth Android desse SHA-1 é criado sozinho; o `ServicesConfig` não muda.
3. Builds de desenvolvimento feitos em **outro computador** usam o `~/.android/debug.keystore` dele: cadastre o SHA-1
   dele do mesmo jeito (`keytool -list -v -keystore ~/.android/debug.keystore -alias androiddebugkey -storepass android`).

### 7.3 AdMob (console do AdMob — não existe CLI para criar apps/blocos)

1. <https://admob.google.com> → **Apps → Adicionar app** (um para Android, outro para iOS; se ainda não publicou,
   escolha "não está listado"). Copie o **ID do app** (`ca-app-pub-…~…`) → `admobAndroidAppId` / `admobIosAppId`.
   O builder copia esses IDs para *Assets → Google Mobile Ads → Settings* (sem isso o app fecha ao abrir no aparelho).
2. Em cada app, crie os **blocos de anúncios** e copie cada ID (`ca-app-pub-…/…`) para o campo correspondente:
   * **Banner** (`bannerAndroid` / `bannerIos`);
   * **Intersticial** (`interstitialAndroid` / `interstitialIos`);
   * **Premiado** (`rewardedAndroid` / `rewardedIos`).
3. Publique o **app-ads.txt** no site do desenvolvedor informado nas lojas (*AdMob → Apps → app-ads.txt*).
4. Classificação de conteúdo (*Bloqueio de conteúdo*): recomendo **G/PG** para combinar com o público casual.
5. Mensagens de privacidade: seção 8.

Enquanto os IDs forem os de teste, builds de release falham de propósito (`BuildIdentityGuard`). Nunca clique nos seus
próprios anúncios reais.

### 7.4 Restringir a chave de API (recomendado)

<https://console.cloud.google.com/apis/credentials?project=potion-pop-game> (conta dona) →
**Browser key (auto created by Firebase)** — é a `firebaseApiKey`:

* Hoje o próprio Firebase já a limitou a 27 APIs do Firebase (incluindo Identity Toolkit, Token Service e Firestore) e
  ela não tem restrição de aplicativo.
* **Restrições de API** → deixe só **Identity Toolkit API** e **Token Service API** (pode manter a **Cloud Firestore
  API**; o jogo não manda a chave nas chamadas ao Firestore). Depois rode o primeiro `curl` da seção 6 de novo.
* **Restrições de aplicativo** → deixe **Nenhuma**. Uma mesma chave não pode ser restrita a Android e iOS ao mesmo
  tempo e o `ServicesConfig` tem um campo só. (O jogo já envia `X-Android-Package`/`X-Android-Cert` e
  `X-Ios-Bundle-Identifier`, caso um dia use chaves separadas por plataforma.)

### 7.5 Conferir a tela de consentimento OAuth

O `deploy --only auth` criou a marca OAuth "Potion Pop!" com o e-mail de suporte `thiagobrunomiranda@gmail.com`.
Confira em *Google Cloud Console → Google Auth Platform → Público*
(<https://console.cloud.google.com/auth/audience?project=potion-pop-game>) que o tipo de usuário é **Externo** e o
status é **Em produção**. Se estiver "Em teste", clique em **Publicar app** (os escopos básicos `openid`/`email`/
`profile` não precisam de verificação do Google). Em *Branding* dá para pôr o ícone e os links de privacidade/termos.

## 8. Mensagens de privacidade (UMP / GDPR / ATT)

O jogo chama o UMP **antes** de inicializar os anúncios (`ConsentInformation.Update` →
`ConsentForm.LoadAndShowConsentFormIfRequired`) e só depois `MobileAds.Initialize`.

1. *AdMob → Privacidade e mensagens → Regulamentações europeias (GDPR)* → **Criar mensagem**: selecione os dois
   apps, idiomas **inglês, português e espanhol**, informe a URL da política de privacidade e **Publique**.
2. *Regulamentações estaduais dos EUA*: crie a mensagem também (recomendado).
3. iOS — **não** publique a *Mensagem explicativa do IDFA*: o jogo não pede rastreamento (sem pedido de ATT; os
   anúncios no iOS são servidos sem o IDFA) e o build não declara `NSUserTrackingUsageDescription` (o pós-processamento
   iOS remove a chave do `Info.plist`). Se um dia quiser pedir o ATT, é preciso voltar a declarar o texto e mudar as
   respostas de privacidade da App Store para "usado para rastrear".
4. Nas **Configurações** do jogo, o botão "Opções de privacidade" aparece quando
   `AdsService.PrivacyOptionsRequired` é `true` e chama `AdsService.ShowPrivacyOptions()`.

## 9. Referência: build, Firestore e testes

### 9.1 Requisitos de build

**Android** (o builder já deixa assim; confira):
* O pacote precisa ser `br.com.brunogames.potionpop` (`PotionPopBuilder.BundleId`): se o Player Settings mostrar outro
  valor, rode **Potion Pop ▸ Rebuild Project** (o `BuildIdentityGuard` recusa builds com outro id).
* *Player Settings → Publishing Settings*: **Custom Main Gradle Template** e **Custom Gradle Properties Template**
  marcados;
* *Assets → External Dependency Manager → Android Resolver → Force Resolve* (puxa `androidx.credentials:credentials`,
  `credentials-play-services-auth` e `googleid`, declarados em `Assets/_Game/Editor/PotionPopDependencies.xml`);
* com *Minify* (R8), a classe `com.potionpop.auth.GoogleSignInBridge` já está anotada com `@Keep`.

**iOS**:
* O pós-processamento (`iOSPostProcess.cs`) grava no `Info.plist` `GIDClientID` (= `googleIosClientId`),
  `GIDServerClientID` (= `googleWebClientId`) e o URL scheme `com.googleusercontent.apps.975002712260-50t6snes3rosvrqf6lj6csb1pnupfr3j`.
  Sem o URL scheme o GoogleSignIn aborta o login (o jogo mostra "opção de login indisponível").
* O pod `GoogleSignIn ~> 8.0` entra pelo EDM4U (mesmo arquivo de dependências). Abra sempre o
  **`Unity-iPhone.xcworkspace`** (não o `.xcodeproj`).

### 9.2 Firestore

As regras ([`firebase/firestore.rules`](../firebase/firestore.rules)) deixam o **save** (`players/{uid}`) visível
**só para o dono**. O ranking global lê a coleção pública `leaderboard/{uid}` (nome, avatar, estrelas, fase), legível
por qualquer jogador logado. Só o dono **grava/exclui** os próprios documentos, com validação de tipos e tamanhos.
`reports/{id}` só aceita **criação** por um jogador logado em nome próprio (denúncia de nome no ranking); ninguém lê,
altera ou apaga pelo app — revise em *Firestore → Dados → reports*. Todo o resto é negado.

Índices: nenhum composto é necessário (o ranking usa `orderBy stars desc limit 50` e uma contagem `stars > N`,
cobertos pelos índices automáticos de campo único).

Documento `players/{uid}`:

| Campo | Tipo | Conteúdo |
|---|---|---|
| `save` | string | `PlayerData` inteiro em JSON |
| `level` | integer | próximo nível |
| `stars` | integer | estrelas totais (ranking global) |
| `name` | string | nome do jogador (até 24 caracteres) |
| `avatar` | string | id do avatar |
| `updatedAt` | integer | unix seconds do último salvamento local |

Linha pública do ranking em `leaderboard/{uid}` (gravada logo depois do save): `name`, `avatar`, `stars`, `level`,
`updatedAt` — sem o save. Denúncia em `reports/{id}`: `reporter` (uid de quem denuncia), `target` (uid denunciado),
`name`, `createdAt`. Excluir a conta apaga `players/{uid}` e `leaderboard/{uid}`.

### 9.3 Testes

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
* Android: `adb logcat -s Unity SPGoogleSignIn` (o segundo é o `TAG` de `Assets/_Game/Plugins/Android/GoogleSignInBridge.java`)
  e procure as tags `[Auth]`, `[CloudSave]`, `[Ads]`, `[Leaderboard]`.
* iOS: console do Xcode, mesmas tags.
* Para ver o formulário GDPR fora da Europa: rode uma vez, copie do log o *hashed device id* do UMP para
  `umpTestDeviceHashedIds` e marque `umpDebugGeographyEea`. Para "resetar" o consentimento, desinstale o app.
* Confira os documentos em *Firestore → Dados → players* (privado) e *leaderboard* (ranking público), e os usuários em
  *Authentication → Usuários*.
* Excluir conta: *Configurações → Conta → Excluir conta* deve apagar os documentos e o usuário em
  *Authentication → Usuários* (o login pode ser pedido de novo — o Firebase exige login recente).

**Erros comuns**

| Sintoma / log | Causa provável |
|---|---|
| Android `[28444] Developer console is not set up correctly` / `nocredential` | SHA-1 do certificado que assinou o APK/AAB não cadastrado (debug de outra máquina, ou o da Assinatura de apps do Play — 7.2), pacote diferente de `br.com.brunogames.potionpop`, ou `googleWebClientId` errado |
| `INVALID_IDP_RESPONSE` no `signInWithIdp` com token real | token emitido para um cliente de outro projeto (use o cliente **Web** deste projeto) |
| `OPERATION_NOT_ALLOWED` | provedor desligado no Authentication (Apple, enquanto 7.1 não for feito) |
| `API key not valid` / `API_KEY_SERVICE_BLOCKED` / `PERMISSION_DENIED … API has not been used` | chave errada ou API fora da restrição da chave (7.4) |
| `[CloudSave] … HTTP 403` | documento fora do formato das regras, ou gravando em `players/` de outro uid |
| iOS: "missing support for the following URL schemes" | `REVERSED_CLIENT_ID` ausente nos URL Types (9.1) |
| Build de release falha com "Google test AdMob id" | IDs do AdMob ainda são os de teste (7.3) |
| Anúncios nunca carregam | IDs reais em app novo (aguarde aprovação/até 1 h), sem consentimento UMP ainda, ou aparelho sem internet — o jogo tenta de novo com espera crescente |

## 10. Checklist de publicação

- [x] Projeto Firebase, Firestore (regras + índices) e login com Google configurados pela CLI.
- [x] `ServicesConfig` com Firebase e clientes OAuth do Google.
- [x] SHA-1/SHA-256 de upload (e de debug deste Mac) cadastrados.
- [ ] SHA-1/SHA-256 da Assinatura de apps do Play cadastrados (7.2).
- [ ] **IDs reais** do AdMob no `ServicesConfig` (7.3) — o builder copia os App IDs para o Google Mobile Ads Settings.
- [ ] Provedor Apple ligado, com Services ID + chave (revogação na exclusão de conta) (7.1).
- [ ] Chave de API restrita (7.4) e tela de consentimento OAuth "Em produção" (7.5).
- [ ] `REVERSED_CLIENT_ID` no Info.plist, capability *Sign in with Apple*, **sem** `NSUserTrackingUsageDescription`
      (conferir no Xcode depois do primeiro build iOS).
- [ ] Mensagens GDPR / estados dos EUA publicadas no AdMob (a de IDFA **não**).
- [ ] `app-ads.txt` no ar; política de privacidade cita Firebase (Auth/Firestore) e AdMob.
- [ ] Formulários de privacidade das lojas (Data safety / App Privacy): identificadores do usuário (uid), e-mail
      (login), dados de jogo, identificadores de publicidade.
- [ ] `Keystore/PotionPop-upload.keystore` + `Keystore/release-signing.json` guardados no cofre.
