# Déployer Wida sur Render gratuit

Ce profil conserve l’API, `InvoiceQueueWorker` et `OriginalRetentionWorker` dans **un même processus .NET et un même conteneur Docker**. PostgreSQL, les clés de session et les originaux sont persistés chez Supabase. RabbitMQ est externe. Aucun worker Render séparé n’est nécessaire.

Il s’agit d’une configuration pour une petite bêta, pas d’une garantie de disponibilité ni de gratuité illimitée. Aucun compte ni ressource cloud n’est créé par le dépôt. Les secrets ci-dessous sont à obtenir et saisir par l’opérateur.

## 1. Services à préparer

### Supabase Free

Créer un projet et conserver son mot de passe PostgreSQL. Copier sa connexion **Session pooler**, port 5432, compatible IPv4. Convertir les champs en chaîne Npgsql, comme dans [`.env.example`](../.env.example). Utiliser TLS avec validation du certificat (`SSL Mode=VerifyFull`), jamais `Trust Server Certificate=true`. Si le fournisseur exige une autorité supplémentaire, monter son certificat et configurer `Root Certificate` dans la chaîne.

Créer un bucket Storage **privé** nommé `wida-originals`. Configurer les types PDF, PNG, JPEG et TIFF. Pour une bêta exclusivement F0, limiter le bucket à 4 Mio par fichier, y compris pour l’administrateur. La limite fournisseur peut alors rejeter un upload admin que l’application autoriserait localement.

Obtenir une clé serveur **`sb_secret_...`** (recommandée) ou l’ancienne clé JWT `service_role`. Ne pas utiliser une clé publishable/anon. La clé nouvelle est transmise dans `apikey` uniquement ; l’ancienne utilise aussi Bearer. Le navigateur ne reçoit aucune de ces clés. L’API effectue les contrôles d’appartenance Wida avant les lectures.

Désactiver la **Data API** si elle n’est pas utilisée. Ne pas exposer les tables Wida, notamment `DataProtectionKeys`, aux rôles anon/authenticated. Wida utilise directement PostgreSQL via EF Core ; ses filtres d’appartenance ne protègent pas une API Supabase indépendante. Storage reste utilisé via son API dédiée.

Ce guide vise une base vide pour le premier déploiement. Changer `Storage:Provider` ne transfère **pas** des fichiers locaux existants. Pour réutiliser une ancienne base, sauvegarder et transférer les originaux puis adapter leurs `StoragePath` et `SizeBytes` avant le changement, ou réimporter explicitement les documents. Un chemin `supabase:...` désigne une clé opaque dans le bucket configuré, jamais une URL publique.

### RabbitMQ hébergé

Une piste gratuite est **CloudAMQP Little Lemur (RabbitMQ)**. Choisir RabbitMQ, pas LavinMQ. Récupérer l’URI **AMQPS** avec le vhost fourni par l’hébergeur ; conserver son chemin et encoder les caractères spéciaux des identifiants si vous construisez l’URI vous-même.

Le profil Render utilise `RabbitMQ__QueueType=classic` et un nouveau nom `wida.invoice-analysis.free.v1`. Cela évite d’exiger les options quorum sur un broker partagé. Les messages sont persistants, les files durables, les publications confirmées, et un seul consommateur est actif avec `prefetch=1`.

**Différence de garanties :** classic n’apporte pas la réplication quorum, et le transfert vers `.failed` est best effort. Les résultats terminaux et erreurs restent enregistrés en PostgreSQL. Le code hors de ce profil conserve `quorum` par défaut. Si votre offre supporte toutes les options quorum, vous pouvez sélectionner ce type avec un **nouveau nom de file**. Ne jamais changer le type d’une file déjà existante ni supprimer une file contenant du travail pour contourner une incompatibilité.

Vérifier le broker avant l’ouverture :

```sh
# Avec Docker installé et un fichier .env rempli et protégé, depuis wida-api :
docker build -t wida-api .
docker run --rm --env-file .env wida-api --check-broker true
```

Cette commande crée/vérifie les deux files sur le vhost configuré, sans publier de travail ni appeler Azure. Elle valide la connexion et les déclarations, mais pas la reprise complète d’une analyse. Vérifier également les politiques imposées par le fournisseur : expiration des files, limites de messages et délai d’acknowledgement. Wida demande 24 heures ; un hébergeur peut imposer ses propres limites. Une connexion interrompue entraîne une reprise à partir de l’état persistant.

### Azure et Google

Utiliser une ressource Document Intelligence **F0**, son endpoint HTTPS et sa clé. Conserver les limitations F0 : 2 pages et 4 Mio par document. Le quota Wida de 400 pages/mois est un budget applicatif ; les administrateurs le contournent et d’autres applications peuvent consommer la même ressource Azure.

Créer/configurer un client Google OAuth de type application Web avec le callback exact :

```text
https://VOTRE_FRONTEND/api/wida/auth/callback
```

C’est le domaine **frontend**, pas le domaine Render de l’API. Le même domaine HTTPS doit être configuré dans `Authentication__PublicOrigin` et `WIDA_PUBLIC_ORIGIN`. Si Google est en mode test, ajouter les comptes test autorisés dans la console Google.

## 2. Générer les secrets locaux

Exécuter ces commandes dans un répertoire privé **hors du dépôt**. Elles produisent les secrets à copier dans les champs de configuration, sans les afficher dans les logs. Choisir le répertoire avant de les lancer ; ne pas écraser des clés déjà utilisées.

```sh
umask 077
openssl rand -base64 48 > proxy-secret.txt
openssl rand -base64 32 > session-password.txt
openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 -nodes \
  -subj '/CN=Wida session protection' -keyout session.key -out session.crt
openssl pkcs12 -export -inkey session.key -in session.crt \
  -out session.pfx -passout file:session-password.txt
openssl base64 -A -in session.pfx -out session-pfx-base64.txt
```

- `proxy-secret.txt` → même valeur dans l’API `Authentication__ProxySecret` et le frontend `WIDA_PROXY_SECRET`.
- `session-pfx-base64.txt` → API `Authentication__DataProtectionCertificateBase64`.
- `session-password.txt` → API `Authentication__DataProtectionCertificatePassword`.

Les clés Data Protection sont enregistrées **chiffrées** en PostgreSQL. Sauvegarder le certificat PFX et son mot de passe séparément de la base. **Les conserver entre les déploiements** : les remplacer sans stratégie de migration empêche le déchiffrement des clés existantes et invalide les sessions. Le certificat autosigné sert au chiffrement des clés internes, pas au HTTPS public (géré par Render).

## 3. Déployer l’API

Publier les changements dans votre dépôt Git, puis créer un **Blueprint Render** depuis le dépôt `wida-api` contenant [`render.yaml`](../render.yaml). Le Blueprint prépare le service Docker gratuit et demande les champs `sync: false`. Une création manuelle de Web Service est possible avec les mêmes variables.

Le Dockerfile utilise le SDK .NET 10 pour compiler et le runtime ASP.NET 10, en utilisateur non-root, pour exécuter `Wida.Api.dll`. Port HTTP interne **10000** ; Render assure le HTTPS public. Aucune clé n’est incluse dans l’image.

`Database__ApplyMigrations=true` applique toutes les migrations au démarrage, avant le lancement des workers, y compris `RemoteStorageAndSessionKeys`. La base doit être accessible et l’utilisateur SQL disposer des droits de migration. Une erreur empêche le démarrage. Cette option vise l’instance unique de la bêta ; pour un déploiement multi-instance, organiser les migrations séparément.

`GET /healthz` répond `200 ok` sans session ni secret proxy. C’est une sonde du processus HTTP, **pas** un diagnostic de PostgreSQL, Storage, Azure ou RabbitMQ. Les autres routes restent protégées. Une panne RabbitMQ peut laisser le site accessible tout en empêchant l’admission des analyses ; surveiller les logs du worker et tester le parcours métier.

## 4. Configurer le frontend

Le frontend doit exécuter Next.js côté serveur, pas un export statique. Configurer ces variables sur son hébergeur, puis redéployer :

```dotenv
WIDA_API_URL=https://VOTRE_API.onrender.com
WIDA_PUBLIC_ORIGIN=https://VOTRE_FRONTEND
WIDA_PROXY_SECRET=MEME_VALEUR_QUE_AUTHENTICATION_PROXYSECRET
WIDA_CLIENT_IP_HEADER=x-real-ip
```

`x-real-ip` n’est approprié que si votre ingress **écrase** réellement cet en-tête avec une seule adresse vérifiée et constitue l’unique accès au frontend. Pour un autre hébergeur, utiliser son en-tête documenté ou configurer le reverse proxy. Ne pas choisir arbitrairement un en-tête transmis librement par le visiteur.

Le secret serveur authentifie le proxy en HTTPS et remplace la liste d’IP fixes côté API pour ce mode. Aucun secret ne doit utiliser un préfixe `NEXT_PUBLIC_`. Une requête directe vers l’API sans le secret est refusée, sauf `/healthz`. Les cookies, l’origine et les jetons CSRF restent vérifiés ; le secret ne remplace pas l’authentification utilisateur.

Sur un premier accès après veille, Render peut demander environ une minute pour démarrer. Le proxy conserve ses délais bornés (60 s généralement, 180 s pour l’admission d’analyse) : une première tentative peut échouer, puis réussir avec « Réessayer ». Les limites de durée et de taille de requête de l’hébergeur frontend s’appliquent aussi ; les vérifier avec un fichier proche de 4 Mio. Ne pas augmenter aveuglément la durée d’une fonction au-delà de ce que son offre permet.

## 5. Récapitulatif des variables API à fournir

Les noms complets figurent aussi dans [`.env.example`](../.env.example). ASP.NET ne lit pas automatiquement ce fichier : Render reçoit les variables dans Environment ; Docker peut utiliser `--env-file`.

| Variable | Valeur / provenance |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | Chaîne Npgsql du Session pooler Supabase, avec mot de passe et TLS |
| `Storage__Supabase__Url` | URL HTTPS du projet Supabase |
| `Storage__Supabase__ServiceKey` | Clé serveur `sb_secret_...` ou ancienne `service_role` |
| `Authentication__PublicOrigin` | Origine HTTPS du frontend, sans chemin |
| `Authentication__Google__ClientId` | Identifiant OAuth Google |
| `Authentication__Google__ClientSecret` | Secret OAuth Google |
| `Authentication__AdminEmail` | Votre adresse Google vérifiée, ou valeur vide pour aucun admin initial |
| `Authentication__ProxySecret` | Secret aléatoire commun aux deux applications |
| `Authentication__DataProtectionCertificateBase64` | PFX encodé en Base64, privé et stable |
| `Authentication__DataProtectionCertificatePassword` | Mot de passe du PFX |
| `RabbitMQ__Uri` | URI AMQPS complète du broker et du vhost |
| `AzureDocumentIntelligence__Endpoint` | Endpoint HTTPS Azure |
| `AzureDocumentIntelligence__Key` | Clé de la ressource Azure F0 |

Le Blueprint fixe déjà : `Storage__Provider=Supabase`, le bucket `wida-originals`, `Authentication__DataProtectionProvider=Database`, `Database__ApplyMigrations=true`, `ProcessingQueue__Enabled=true`, `RabbitMQ__QueueType=classic`, un nom de file propre à ce profil, `Authentication__PublicBeta=true`, `AzureDocumentIntelligence__Tier=F0` et le port 10000. Si le bucket porte un autre nom, changer aussi `Storage__Supabase__Bucket`.

Turnstile reste optionnel : si activé, reprendre les variables de [public-beta.md](public-beta.md). Aucun token ou secret réel n’est fourni par le code.

## Test Docker local reproductible

Avec Docker Desktop actif, Python 3 et OpenSSL, depuis `wida-api` :

```sh
python3 deploy/smoke-test.py
```

Le script construit l’image, lance PostgreSQL et RabbitMQ dans un réseau temporaire, puis vérifie : migrations réelles, endpoint de santé, utilisateur non-root, protection du proxy, clés de session chiffrées, abonnement du worker et reprise après redémarrage. Le conteneur API est limité à 512 Mio de mémoire et 0,5 CPU ; son port est exposé uniquement sur `127.0.0.1`. Les services, volumes, réseau et secrets de test sont supprimés à la fin ; l’image construite reste disponible. `--skip-build` réutilise l’image `wida-api:smoke-test`.

Ce test utilise le stockage local temporaire et ne contacte ni Supabase, ni Google, ni Azure. Il ne valide pas une analyse OCR complète ou une connexion aux services cloud.

## 6. Validation avant partage

Avec des factures fictives et deux comptes ordinaires :

1. Vérifier les logs de migration, `/healthz`, puis le refus d’une route API appelée sans secret proxy.
2. Se connecter via Google, uploader, prévisualiser (y compris une requête Range) et télécharger l’original.
3. Lancer l’analyse, attendre la fin, corriger, sauvegarder puis recharger.
4. Vérifier qu’un deuxième compte ne peut lire ni le document ni ses résultats.
5. Redéployer : vérifier que session et original restent accessibles.
6. Redémarrer pendant une analyse : vérifier la reprise d’un identifiant Azure connu. Une soumission sans identifiant sauvegardé doit rester signalée comme incertaine, sans nouvelle soumission automatique.
7. Laisser le service s’endormir, revenir et tester la reprise. Sans requête entrante, aucun traitement ni nettoyage n’est garanti pendant la veille.
8. Sauvegarder PostgreSQL, les objets Storage et le certificat de session, puis tester une restauration isolée.

Le nettoyage des originaux expirés s’exécute au réveil puis chaque heure pendant que l’API tourne. L’accès aux originaux expirés est refusé immédiatement selon leur date ; la suppression physique peut être retardée par la veille. Les analyses actives et les originaux admin sont exclus du nettoyage. Les uploads orphelins après panne réseau/commit incertain demandent une réconciliation opérateur : ne supprimer que les objets non référencés après vérification de la base.

## Limites gratuites et sources

Vérifiées le 13 septembre 2026, à revérifier avant création des comptes :

- [Render Free](https://render.com/docs/free) : veille après 15 minutes sans trafic entrant, disque éphémère, 750 heures partagées par workspace. Un trafic sortant inhabituellement élevé peut suspendre le service. Les dépassements peuvent être facturés si un moyen de paiement est configuré ; examiner les limites de dépenses. Ne pas multiplier les services gratuits supposés actifs en continu dans le même quota.
- [Supabase Free](https://supabase.com/pricing) : 500 Mo de base, 1 Go d’originaux et quotas de transfert ; pause possible après inactivité. Les résultats Azure bruts occupent aussi la base. Prévoir ses propres sauvegardes adaptées à l’offre.
- [CloudAMQP](https://www.cloudamqp.com/plans.html) : Little Lemur gratuit, broker RabbitMQ partagé, 20 connexions, 1 million de messages/mois et durée d’inactivité maximale des files annoncée à 28 jours. Vérifier les conditions exactes de l’instance obtenue et exécuter le preflight ; la compatibilité distante n’est pas établie par les tests unitaires.
- [Azure F0](https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/service-limits?view=doc-intel-4.0.0) : limitations de pages, tailles et cadence propres à la ressource. Ne pas basculer en S0 si le budget doit rester gratuit.
- [Clés Supabase](https://supabase.com/docs/guides/getting-started/api-keys), [Session pooler](https://supabase.com/docs/guides/troubleshooting/supavisor-and-connection-terminology-explained-9pr_ZO) et [Data Protection EF Core](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0#persist-keys-in-a-database-persistkeystodbcontext).

Les tests automatisés utilisent des réponses HTTP simulées et des bases locales de test. Ils ne certifient pas l’accès à votre Supabase, Google, RabbitMQ ou Azure. Le Dockerfile a été construit et testé localement le 14 septembre 2026 sur Linux/amd64 avec Docker Desktop. Le test de démarrage et redémarrage a réussi avec PostgreSQL 17, RabbitMQ 4.2, 512 Mio de mémoire et 0,5 CPU : migrations, chiffrement et réutilisation des clés, protection du proxy et reconnexion du worker. Ce résultat ne couvre pas Supabase, Google, Azure ni une analyse OCR complète. Le test est reproductible avec `python3 deploy/smoke-test.py`.
