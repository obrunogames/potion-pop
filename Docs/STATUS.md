# Status do projeto (2026-10-03)

## Pronto e verificado
- **Jogo completo jogável** no Unity 6000.6.3f1: Home, partida (garrafas com líquido de verdade — a superfície fica
  nivelada enquanto a garrafa inclina —, despejo animado com fio de poção, respingos e bolhas, rolha nas garrafas
  completas, combos, cores escondidas "?", garrafas de pedra com contador, fases difíceis, tutorial), 4 reforços na
  fase (Desfazer, Embaralhar, Garrafa Extra, Varinha) + 2 antes da fase (Poção Arco-íris, Bola de Cristal),
  estrelas por jogadas (1–3), sem jogadas / continuar com +1 garrafa, vitória/derrota, vidas, moedas, prêmio diário,
  roleta, baú de estrelas, missões, coleção (6 álbuns × 9 cartas), loja, ranking (semanal + global), perfil,
  configurações, login Google/Apple (Firebase REST) com save na nuvem, AdMob + UMP.
- **Tela de Mundos** (grupos de fases): 6 mundos de 20 fases com arte própria, estrelas por mundo, mapa de fases com
  caminho, estrelas de cada fase, rejogar fases já vencidas (só as estrelas novas dão moedas).
- **Fases infinitas** geradas e **verificadas como solucionáveis** (solucionador A* com heurística admissível):
  fases 1–1000 válidas e amostras até a fase 300.000 (a solução encontrada é reproduzida até a vitória), mediana
  ~7 ms, p90 ~105 ms, máx. ~360 ms no desktop; a próxima fase é gerada em segundo plano. Depois da fase 120 os 6
  mundos se repetem com numeral ("Floresta Encantada II"); a dificuldade sobe até ~fase 100 e depois varia entre 10
  e 12 cores. Veredito "sem saída" do solucionador conferido contra busca
  exaustiva (27/27).
- **Arte**: 97 imagens novas geradas no Codex (mundos, 54 cartas, Luna em 5 poses, reforços, ícones, rolha, pedra,
  logo, ícone do app) + o kit de UI do Shelf Pop (mesmo design system); garrafas de vidro desenhadas por código a
  partir de `Resources/bottle_shape.json`, alinhadas ao pixel com a malha do líquido.
- **Áudio**: 40 efeitos + 3 músicas sintetizados (`Tools/gen_audio.py`).
- **Idiomas**: pt-BR, en, es (todas as chaves nos 3 idiomas).
- **Testes**: 122 EditMode passando (regras das garrafas, solucionador, fases 1–300, estrelas, rejogar, save, economia,
  missões, coleção, idiomas, serviços).
- **QA no editor** (`Potion Pop ▸ QA`): tour automático com capturas de todas as telas e popups em 1080×2340
  (`Screenshots/qa/`), fases jogadas sozinhas pelo solucionador (inclusive fase difícil com cores escondidas e pedra),
  fluxo vitória → próxima fase → nova carta → próxima fase.
- **Build Android de teste OK**: APK de desenvolvimento gerado em modo batch numa cópia do projeto
  (`python3 Tools/release/build_release.py android-dev` → `Builds/dev/PotionPop-1.0.0-dev.apk`, 54 MB) — Gradle,
  dependências do AdMob e do login Google resolvidas.
- **Firebase** `potion-pop-game` configurado pela CLI (Firestore + regras, login Google, apps Android/iOS com SHA-1/256,
  chave de upload Android em `Keystore/`, fora do git). Detalhes: `Docs/Firebase-Setup.md`.

## Lojas (04/10/2026)
- **Enviado para revisão no Google Play e na App Store** (1.0.0, build 1), lançamento automático depois da
  aprovação; TestFlight interno com o dono. Ids, estado e o que falta: `Docs/loja/lojas.md`.

## Pendente (manual / próximos passos)
1. Acompanhar a revisão das lojas; depois: AdMob › Adicionar loja e links das lojas no site (`Docs/loja/lojas.md`).
2. **Login com a Apple**: o provedor está ligado no Firebase; falta a seção de revogação (Services ID + chave), ver
   `Docs/Firebase-Setup.md` 7.1.
3. **Nome**: "Potion Pop!" está nas lojas; trocar exigiria nova ficha e nova versão.
4. Revisar o equilíbrio de dificuldade/estrelas jogando de verdade (limites em `LevelGenerator.Finish` e curva em
   `Difficulty.For`).
