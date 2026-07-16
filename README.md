# SupplyGuard: XAI-Powered Smart Early Warning and Supplier Evaluation System

## 📌 Project Overview
SupplyGuard is an enterprise-level risk management platform designed to predict and mitigate supply chain disruptions. While standard AI models simply flag a supplier as "risky," SupplyGuard integrates **Explainable AI (XAI)** to provide transparent, data-driven reasons behind every risk score. This ensures full data transparency and accountability for large industrial and defense organizations.

## 🏗️ Architecture
This project is strictly built on **Clean Architecture (Onion Architecture)** principles. The domain logic is completely isolated from external frameworks, ensuring a highly testable and maintainable codebase. 

The application utilizes the **CQRS Pattern** via MediatR to segregate read and write operations, optimizing performance and scalability.

## 🚀 Tech Stack
* **Backend:** .NET 8, ASP.NET Core Web API
* **Architecture:** Clean Architecture, CQRS, Repository Pattern
* **Database & ORM:** PostgreSQL, Entity Framework Core (Code-First)
* **Caching & Messaging:** Redis (Distributed Caching), RabbitMQ (Message Broker)
* **AI & Big Data:** Apache Spark, XAI (Explainable AI) Integrations
* **Security:** ASP.NET Core Identity, JWT, Role/Claim Based Authorization
* **DevOps & Testing:** Docker, Docker Compose, xUnit, Moq

## 🔑 Key Features
* **Proactive Early Warning:** Asynchronous alert mechanisms (via RabbitMQ) to warn stakeholders about potential supplier delays or financial instabilities.
* **XAI Audit Logging:** An append-only `XAIAuditLog` mechanism that records every AI invocation (Model Version, Request/Response payloads, Confidence Score, and Latency) to ensure historical traceability and debugging.
* **High Performance:** Redis implementation for caching frequently accessed supplier data and risk indicators to minimize database load.

## 👨‍💻 Developer
**Fatih Altunel** | Software Engineer specializing in the .NET ecosystem, Clean Architecture, and enterprise-level system modernization.