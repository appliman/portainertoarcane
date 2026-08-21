# PortainerToArcane

Assistant TUI .NET 10 pour préparer une migration Portainer vers Arcane, étape par étape et sans arrêter automatiquement les conteneurs.

Fonctions principales :

- lecture directe d'un backup Portainer tar.gz ;
- inventaire des endpoints et stacks ;
- contrôle facultatif de l'API Portainer ;
- analyse des risques Compose ;
- sélection des stacks à migrer ;
- export par endpoint vers une arborescence compatible avec le Projects Directory d'Arcane ;
- manifeste, session de reprise et garde-fous contre les secrets.

Le programme n'exécute aucune suppression, commande down ou bascule automatique.

## Développement

Commande de compilation : dotnet build PortainerToArcane.slnx

Commande de test : dotnet test PortainerToArcane.slnx

Commande de lancement :

    dotnet run --project src/PortainerToArcane -- --backup chemin-du-backup.tar.gz

Exemple avec contrôle live de Portainer :

    dotnet run --project src/PortainerToArcane -- \
      --backup portainer-backup.tar.gz \
      --portainer-url http://portainer.example.com:9000 \
      --api-key-env PORTAINER_API_KEY

Analyse sans exporter les Compose et les secrets :

    dotnet run --project src/PortainerToArcane -- \
      --backup portainer-backup.tar.gz \
      --all --analyze-only --non-interactive

Export non interactif :

    dotnet run --project src/PortainerToArcane -- \
      --backup portainer-backup.tar.gz \
      --output arcane-projects \
      --all --non-interactive

La clé API est lue uniquement depuis la variable d'environnement indiquée. Elle n'est jamais écrite dans la session ou les journaux.

## Sécurité

- Les archives, fichiers .env, certificats et clés privées sont ignorés par Git.
- Les fichiers .env exportés reçoivent des permissions utilisateur uniquement sous Linux.
- L'application ne lance aucune commande Docker destructive.
- Le répertoire exporté contient des secrets et doit rester hors Git.
