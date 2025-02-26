# 📌 Social Network App

Une application de réseau social utilisant SQL Server et Neo4j pour la gestion des utilisateurs, des abonnements et des achats de produits.

## 🚀 Fonctionnalités

- 📋 Gestion des utilisateurs : Ajout et suivi des utilisateurs.
- 🔗 Système de followers : Relations entre utilisateurs avec suivi multi-niveaux.
- 🛒 Achats de produits : Gestion des produits achetés par les utilisateurs.
- 📊 Analyse des performances : Comparaison des temps de requête entre SQL Server et Neo4j.

## 🛠️ Technologies utilisées

- SQL Server : Gestion relationnelle des données
- Neo4j : Base de données orientée graphe
- C# : Application client lourd et exécution des requêtes
- Cypher & SQL : Langages de requêtes pour la base de données

## ⚡ Installation

### 1️⃣ Prérequis

- .NET 6+
- SQL Server
- Neo4j (avec Neo4j Desktop ou une instance cloud)

### 2️⃣ Configuration

- SQL Server : Se connecter à la base de données localhost//SQLEXPRESS et créer la base de données SocialNetworkDB. Pour voir les requêtes à éxécuter, voir le fichier SQLServeurScript.txt.
- Neo4j : Se connecter avec l'utilisateur neo4j et le mot de passe INFRES_XV. Pour voir les requêtes à éxécuter, voir le fichier Neo4JScript.txt.

### 3️⃣ Exécution

```bash
dotnet restore
dotnet build
dotnet run
```
## Auteurs

BOUHENAF Irfan et JEULIN Elian
