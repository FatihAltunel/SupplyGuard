# SupplyGuard: XAI-Powered Smart Early Warning and Supplier Evaluation System

## 📌 Project Overview
[cite_start]SupplyGuard is an enterprise-level risk management platform designed to predict and mitigate supply chain disruptions[cite: 188, 190]. [cite_start]While standard AI models simply flag a supplier as "risky," SupplyGuard integrates **Explainable AI (XAI)** to provide transparent, data-driven reasons behind every risk score[cite: 191, 193]. [cite_start]This ensures full data transparency and accountability for large industrial and defense organizations[cite: 75, 193].

## 🏗️ Architecture
[cite_start]This project is strictly built on **Clean Architecture (Onion Architecture)** principles[cite: 136]. [cite_start]The domain logic is completely isolated from external frameworks, ensuring a highly testable and maintainable codebase[cite: 136]. 

[cite_start]The application utilizes the **CQRS Pattern** via MediatR to segregate read and write operations, optimizing performance and scalability[cite: 136].

## 🚀 Tech Stack
* [cite_start]**Backend:** .NET 8, ASP.NET Core Web API [cite: 136]
* [cite_start]**Architecture:** Clean Architecture, CQRS, Repository Pattern [cite: 136]
* [cite_start]**Database & ORM:** PostgreSQL, Entity Framework Core (Code-First) [cite: 136]
* [cite_start]**Caching & Messaging:** Redis (Distributed Caching), RabbitMQ (Message Broker) [cite: 136]
* [cite_start]**AI & Big Data:** Apache Spark, XAI (Explainable AI) Integrations [cite: 136]
* [cite_start]**Security:** ASP.NET Core Identity, JWT, Role/Claim Based Authorization [cite: 136]
* [cite_start]**DevOps & Testing:** Docker, Docker Compose, xUnit, Moq [cite: 136]

## 🔑 Key Features
* [cite_start]**Proactive Early Warning:** Asynchronous alert mechanisms (via RabbitMQ) to warn stakeholders about potential supplier delays or financial instabilities[cite: 136, 189].
* [cite_start]**XAI Audit Logging:** An append-only `XAIAuditLog` mechanism that records every AI invocation (Model Version, Request/Response payloads, Confidence Score, and Latency) to ensure historical traceability and debugging[cite: 294, 296, 301].
* [cite_start]**High Performance:** Redis implementation for caching frequently accessed supplier data and risk indicators to minimize database load[cite: 136].

## 👨‍💻 Developer
**Fatih Altunel** Software Engineer specializing in the .NET ecosystem, Clean Architecture, and enterprise-level system modernization.