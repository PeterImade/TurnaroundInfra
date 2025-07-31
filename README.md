# Turnaround Prompt API

This project is an AWS Cloud infrastructure stack built with **AWS CDK in C#**. It provisions a RESTful API integrated with DynamoDB using **API Gateway**, fully secured with API keys and equipped with JSON Schema validation, VTL mappings, and conditional logic for data integrity.

---

## 📌 Features

- ✅ **API Gateway** with full CRUD support (`GET`, `PUT`, `PATCH`, `DELETE`)
- ✅ **DynamoDB** table with `PayPerRequest` billing
- ✅ **API Key Authentication** with Usage Plans
- ✅ **Request validation** using JSON schema
- ✅ **Request/Response Mapping** using Velocity Templates (VTL)
- ✅ **Safe updates/deletes** with conditional expressions
- ✅ **CloudWatch logging** for API calls

---

## 🔧 Stack Components

- **DynamoDB Table**: `TurnaroundPrompt`  
  - Partition key: `id` (string)
  - Soft deletes via a `deleted` boolean flag

- **API Gateway**
  - Resource: `/turnaroundprompt/{id}`
  - Methods: `GET`, `PUT`, `PATCH`, `DELETE`
  - All methods require API Key
  - Logging enabled to CloudWatch
  - CORS enabled for all origins and methods

- **IAM Roles**
  - API Gateway integration role with DynamoDB access
  - API Gateway log push role to CloudWatch

---

## 🔐 API Keys

| Key Name   | Value                    | Purpose           |
|------------|--------------------------|--------------------|
| `apiKey1`  | `readonly-key-1234567890`| Read-only access   |
| `apiKey2`  | `admin-key-9876543210`   | Admin access       |

Both keys are attached to the same usage plan.

---

## 🧪 Request Validation

The API uses **request models** to validate input for `PUT` and `PATCH` operations:

```json
{
  "name": "Example Prompt",
  "status": "pending"
}
