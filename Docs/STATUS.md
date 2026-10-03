# Status do projeto (2026-10-02)

## Pronto e verificado
- **Jogo completo jogável** no Unity 6000.6.3f1: Home, partida (tabuleiro, arraste/toque, combos, camadas, dispensers,
  prateleiras trancadas, fases difíceis, tutorial), 4 reforços + 2 pré-fase, vitória/derrota/continuar, vidas, moedas,
  estrelas, prêmio diário, roleta, baú de estrelas, missões, coleção (5 álbuns), loja, ranking (semanal + global),
  perfil, configurações, login Google/Apple (Firebase REST) com save na nuvem, AdMob + UMP.
- **Fases infinitas** geradas e verificadas como solucionáveis; variedade alta (cada trio um produto diferente a partir
  da fase 20; ~1 trio pronto visível na abertura); próxima fase gerada em segundo plano.
- **Arte**: 173 imagens geradas no Codex + primitivas de UI; **áudio**: 35 efeitos + 3 músicas sintetizados.
- **Idiomas**: pt-BR, en, es (todas as chaves nos 3 idiomas).
- **Moderação do ranking global** (diretriz 1.2 da Apple): nomes passam por um filtro de palavrões/ofensas em
  pt/en/es (`NameFilter`; o próprio nome é recusado ao editar e nomes ofensivos de outros jogadores aparecem como
  "Jogador"); tocar na linha de outro jogador real permite **denunciar o nome** (documento em `reports` no Firestore,
  ou e-mail ao suporte se não der para enviar) e **bloquear o jogador** (some do ranking na hora; salvo no save).
  Denúncias são revisadas no console do Firebase (*Firestore → reports*).
- **Sem rastreamento (ATT)**: o jogo nunca mostra o pedido de ATT e o build iOS não declara
  `NSUserTrackingUsageDescription`; anúncios no iOS sem IDFA.
- **Testes**: 121 EditMode passando (regras, solubilidade 1–300, dicas, variedade, save/merge de nuvem, economia,
  idiomas, serviços, filtro de nomes/moderação).
- **QA no editor** com capturas de todas as telas/popups em 1080×2340, 1080×1920 (16:9) e 1536×2048 (tablet)
  (`Screenshots/qa/`), partidas completas automáticas (`Assets/_Game/Editor/QaTour.cs`).
- **Auditoria multiagente** (economia, persistência, layout, i18n, desempenho, plataforma): 23 problemas corrigidos.
- **Site**: página do jogo, política de privacidade e suporte (pt/en/es) no brunogames.com.br/jogos/potion-pop.

## Lojas (02/10/2026)
**Enviado para revisão** na App Store (Apple ID 6818572338) e no Google Play (app 4973065135885633899), versão
1.0.0 (1), lançamento automático depois da aprovação. Firebase (`potion-pop-game`) e AdMob reais configurados.
Detalhes, ids, credenciais, scripts e o que falta (acompanhar a revisão, links no site, AdMob › Adicionar loja,
conferir denúncias de nomes no Firestore, renovar o Apple Developer até 14/10): `Docs/loja/lojas.md`.
