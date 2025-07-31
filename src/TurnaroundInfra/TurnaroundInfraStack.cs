using Amazon.CDK;
using Amazon.CDK.AWS.APIGateway;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using Constructs;
using System.Collections.Generic;

namespace TurnaroundInfra
{
    public class TurnaroundInfraStack : Stack
    {
        internal TurnaroundInfraStack(Amazon.CDK.Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {
            var table = new Table(this, "TurnaroundPromptTable", new TableProps
            {
                TableName = "TurnaroundPrompt",
                PartitionKey = new Attribute { Name = "id", Type = AttributeType.STRING },
                BillingMode = BillingMode.PAY_PER_REQUEST,
                RemovalPolicy = RemovalPolicy.DESTROY
            });

            var logGroup = new LogGroup(this, "TurnaroundApiLogs", new LogGroupProps
            {
                LogGroupName = "/aws/apigateway/TurnaroundPromptApi",
                Retention = RetentionDays.ONE_WEEK,
                RemovalPolicy = RemovalPolicy.DESTROY
            });

            var apiGatewayLogRole = new Role(this, "ApiGatewayCloudWatchRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("apigateway.amazonaws.com"),
                ManagedPolicies = new[]
                {
                    ManagedPolicy.FromAwsManagedPolicyName("service-role/AmazonAPIGatewayPushToCloudWatchLogs")
                }
            });

            new CfnAccount(this, "ApiGatewayAccount", new CfnAccountProps
            {
                CloudWatchRoleArn = apiGatewayLogRole.RoleArn
            });

            var dynamoRole = new Role(this, "DynamoDBRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("apigateway.amazonaws.com")
            });
            table.GrantReadWriteData(dynamoRole);

            var api = new RestApi(this, "TurnaroundPromptApi", new RestApiProps
            {
                RestApiName = "TurnaroundPromptApi",
                Description = "API to interact with turnaround prompt items.",
                DeployOptions = new StageOptions
                {
                    StageName = "prod",
                    LoggingLevel = MethodLoggingLevel.INFO,
                    DataTraceEnabled = true,
                    MetricsEnabled = true,
                    AccessLogDestination = new LogGroupLogDestination(logGroup),
                    AccessLogFormat = AccessLogFormat.JsonWithStandardFields(new JsonWithStandardFieldProps
                    {
                        Caller = true,
                        HttpMethod = true,
                        Ip = true,
                        Protocol = true,
                        RequestTime = true,
                        ResourcePath = true,
                        ResponseLength = true,
                        Status = true,
                        User = true
                    })
                },
                DefaultCorsPreflightOptions = new CorsOptions
                {
                    AllowOrigins = Cors.ALL_ORIGINS,
                    AllowMethods = Cors.ALL_METHODS,
                    AllowHeaders = new[] { "Content-Type", "X-Api-Key" }
                }
            });

            // API Keys for authentication
            var apiKey1 = api.AddApiKey("ReadOnlyKey", new ApiKeyOptions
            {
                ApiKeyName = "apiKey1",
                Value = "readonly-key-1234567890"
            });

            var apiKey2 = api.AddApiKey("AdminKey", new ApiKeyOptions
            {
                ApiKeyName = "apiKey2",
                Value = "admin-key-9876543210"
            });

            // Usage Plan
            var usagePlan = api.AddUsagePlan("BasicPlan", new UsagePlanProps
            {
                Name = "TurnaroundPromptUsagePlan",
                ApiStages = new[]
                {
                    new UsagePlanPerApiStage { Api = api, Stage = api.DeploymentStage }
                }
            });
            usagePlan.AddApiKey(apiKey1);
            usagePlan.AddApiKey(apiKey2);

            // JSON Schema Model for validation
            var requestModel = new Model(this, "TurnaroundPromptModel", new ModelProps
            {
                RestApi = api,
                ContentType = "application/json",
                ModelName = "TurnaroundPromptModel",
                Schema = new JsonSchema
                {
                    Schema = JsonSchemaVersion.DRAFT4,
                    Type = JsonSchemaType.OBJECT,

                    Properties = new Dictionary<string, Amazon.CDK.AWS.APIGateway.IJsonSchema>
                    {
                        ["id"] = new Amazon.CDK.AWS.APIGateway.JsonSchema
                        {
                            Type = JsonSchemaType.STRING,
                            Pattern = "^TAP-\\d+$"
                        },
                        ["name"] = new Amazon.CDK.AWS.APIGateway.JsonSchema
                        {
                            Type = JsonSchemaType.STRING,
                            MinLength = 1,
                            MaxLength = 255
                        },
                        ["status"] = new Amazon.CDK.AWS.APIGateway.JsonSchema
                        {
                            Type = JsonSchemaType.STRING,
                            Enum = new[] { "active", "inactive", "pending", "completed" }
                        }
                    },
                    Required = new[] { "id", "name", "status" }
                }
            });

            var promptResource = api.Root.AddResource("turnaroundprompt");
            var promptById = promptResource.AddResource("{id}");

            var getIntegration = new AwsIntegration(new AwsIntegrationProps
            {
                Service = "dynamodb",
                Action = "GetItem",
                IntegrationHttpMethod = "POST",
                Options = new IntegrationOptions
                {
                    CredentialsRole = dynamoRole,
                    RequestTemplates = new Dictionary<string, string>
                    {
                        ["application/json"] = @$"{{
                            ""TableName"": ""{table.TableName}"",
                            ""Key"": {{
                                ""id"": {{""S"": ""$input.params('id')""}}
                            }},
                            ""ConsistentRead"": true
                        }}"
                    },
                    IntegrationResponses = new[]
                    {
                        new IntegrationResponse
                        {
                            StatusCode = "200",
                            ResponseTemplates = new Dictionary<string, string>
                            {
                                ["application/json"] = @"#set($inputRoot = $input.path('$'))
#if($inputRoot.Item && $inputRoot.Item.id.S && $inputRoot.Item.deleted.BOOL == false)
{
  ""id"": ""$inputRoot.Item.id.S"",
  ""name"": ""$inputRoot.Item.name.S"",
  ""status"": ""$inputRoot.Item.status.S"",
  ""deleted"": $inputRoot.Item.deleted.BOOL
}
#else
{
  ""message"": ""Item not found""
}
#end"
                            }
                        },
                        new IntegrationResponse
                        {
                            StatusCode = "400",
                            SelectionPattern = "4\\d{2}",
                            ResponseTemplates = new Dictionary<string, string>
                            {
                                ["application/json"] = @"{""message"": ""Invalid request""}"
                            }
                        },
                        new IntegrationResponse
                        {
                            StatusCode = "500",
                            SelectionPattern = "5\\d{2}",
                            ResponseTemplates = new Dictionary<string, string>
                            {
                                ["application/json"] = @"{""message"": ""Internal server error""}"
                            }
                        }
                    }
                }
            });

            promptById.AddMethod("GET", getIntegration, new MethodOptions
            {
                ApiKeyRequired = true,
                MethodResponses = new[]
                {
                    new MethodResponse { StatusCode = "200" },
                    new MethodResponse { StatusCode = "400" },
                    new MethodResponse { StatusCode = "500" }
                }
            });

            var putIntegration = new AwsIntegration(new AwsIntegrationProps
            {
                Service = "dynamodb",
                Action = "PutItem",
                IntegrationHttpMethod = "POST",
                Options = new IntegrationOptions
                {
                    CredentialsRole = dynamoRole,
                    RequestTemplates = new Dictionary<string, string>
                    {
                        ["application/json"] = @$"{{
                            ""TableName"": ""{table.TableName}"",
                            ""Item"": {{
                                ""id"": {{""S"": ""$input.json('$.id')""}},
                                ""name"": {{""S"": ""$input.json('$.name')""}},
                                ""status"": {{""S"": ""$input.json('$.status')""}},
                                ""deleted"": {{""BOOL"": false}}
                            }},
                            ""ConditionExpression"": ""attribute_not_exists(id)""
                        }}"
                    },
                    IntegrationResponses = new[]
                    {
                        new IntegrationResponse
                        {
                            StatusCode = "201",
                            ResponseTemplates = new Dictionary<string, string>
                            {
                                ["application/json"] = @"{
  ""message"": ""Item created successfully"",
  ""id"": ""$input.json('$.id')"",
  ""name"": ""$input.json('$.name')"",
  ""status"": ""$input.json('$.status')"",
  ""deleted"": false
}"
                            }
                        },
                        new IntegrationResponse
                        {
                            StatusCode = "400",
                            SelectionPattern = ".*ConditionalCheckFailedException.*",
                            ResponseTemplates = new Dictionary<string, string>
                            {
                                ["application/json"] = @"{""message"": ""Item already exists""}"
                            }
                        },
                        new IntegrationResponse
                        {
                            StatusCode = "500",
                            SelectionPattern = "5\\d{2}",
                            ResponseTemplates = new Dictionary<string, string>
                            {
                                ["application/json"] = @"{""message"": ""Internal server error""}"
                            }
                        }
                    }
                }
            });

            promptResource.AddMethod("PUT", putIntegration, new MethodOptions
            {
                ApiKeyRequired = true,
                RequestModels = new Dictionary<string, IModel>
                {
                    ["application/json"] = requestModel
                },
                RequestValidatorOptions = new RequestValidatorOptions
                {
                    ValidateRequestBody = true,
                    ValidateRequestParameters = false
                },
                MethodResponses = new[]
                {
                    new MethodResponse { StatusCode = "201" },
                    new MethodResponse { StatusCode = "400" },
                    new MethodResponse { StatusCode = "500" }
                }
            });

            var patchIntegration = new AwsIntegration(new AwsIntegrationProps
            {
                Service = "dynamodb",
                Action = "UpdateItem",
                IntegrationHttpMethod = "POST",
                Options = new IntegrationOptions
                {
                    CredentialsRole = dynamoRole,
                    RequestTemplates = new Dictionary<string, string>
                    {
                        ["application/json"] = @$"{{
                            ""TableName"": ""{table.TableName}"",
                            ""Key"": {{
                                ""id"": {{""S"": ""$input.json('$.id')""}}
                            }},
                            ""UpdateExpression"": ""SET #n = :n, #s = :s"",
                            ""ExpressionAttributeNames"": {{
                                ""#n"": ""name"",
                                ""#s"": ""status""
                            }},
                            ""ExpressionAttributeValues"": {{
                                "":n"": {{""S"": ""$input.json('$.name')""}},
                                "":s"": {{""S"": ""$input.json('$.status')""}},
                                "":false"": {{""BOOL"": false}}
                            }},
                            ""ConditionExpression"": ""attribute_exists(id) AND deleted = :false"",
                            ""ReturnValues"": ""ALL_NEW""
                        }}"
                    },
                    IntegrationResponses = new[]
                    {
                        new IntegrationResponse
                        {
                            StatusCode = "200",
                            ResponseTemplates = new Dictionary<string, string>
                            {
                                ["application/json"] = @"{
  ""message"": ""Item updated successfully"",
  ""id"": ""$input.path('$.Attributes.id.S')"",
  ""name"": ""$input.path('$.Attributes.name.S')"",
  ""status"": ""$input.path('$.Attributes.status.S')"",
  ""deleted"": $input.path('$.Attributes.deleted.BOOL')
}"
                            }
                        },
                        new IntegrationResponse
                        {
                            StatusCode = "404",
                            SelectionPattern = ".*ConditionalCheckFailedException.*",
                            ResponseTemplates = new Dictionary<string, string>
                            {
                                ["application/json"] = @"{""message"": ""Item not found or already deleted""}"
                            }
                        },
                        new IntegrationResponse
                        {
                            StatusCode = "500",
                            SelectionPattern = "5\\d{2}",
                            ResponseTemplates = new Dictionary<string, string>
                            {
                                ["application/json"] = @"{""message"": ""Internal server error""}"
                            }
                        }
                    }
                }
            });

            promptResource.AddMethod("PATCH", patchIntegration, new MethodOptions
            {
                ApiKeyRequired = true,
                RequestModels = new Dictionary<string, IModel>
                {
                    ["application/json"] = requestModel
                },
                RequestValidatorOptions = new RequestValidatorOptions
                {
                    ValidateRequestBody = true,
                    ValidateRequestParameters = false
                },
                MethodResponses = new[]
                {
                    new MethodResponse { StatusCode = "200" },
                    new MethodResponse { StatusCode = "404" },
                    new MethodResponse { StatusCode = "500" }
                }
            });

            var deleteIntegration = new AwsIntegration(new AwsIntegrationProps
            {
                Service = "dynamodb",
                Action = "UpdateItem",
                IntegrationHttpMethod = "POST",
                Options = new IntegrationOptions
                {
                    CredentialsRole = dynamoRole,
                    RequestTemplates = new Dictionary<string, string>
                    {
                        ["application/json"] = @$"{{
                            ""TableName"": ""{table.TableName}"",
                            ""Key"": {{
                                ""id"": {{""S"": ""$input.params('id')""}}
                            }},
                            ""UpdateExpression"": ""SET deleted = :d"",
                            ""ExpressionAttributeValues"": {{
                                "":d"": {{""BOOL"": true}},
                                "":false"": {{""BOOL"": false}}
                            }},
                            ""ConditionExpression"": ""attribute_exists(id) AND deleted = :false"",
                            ""ReturnValues"": ""ALL_NEW""
                        }}"
                    },
                    IntegrationResponses = new[]
                    {
                        new IntegrationResponse
                        {
                            StatusCode = "200",
                            ResponseTemplates = new Dictionary<string, string>
                            {
                                ["application/json"] = @"{
  ""message"": ""Item deleted successfully"",
  ""id"": ""$input.path('$.Attributes.id.S')"",
  ""deleted"": $input.path('$.Attributes.deleted.BOOL')
}"
                            }
                        },
                        new IntegrationResponse
                        {
                            StatusCode = "404",
                            SelectionPattern = ".*ConditionalCheckFailedException.*",
                            ResponseTemplates = new Dictionary<string, string>
                            {
                                ["application/json"] = @"{""message"": ""Item not found or already deleted""}"
                            }
                        },
                        new IntegrationResponse
                        {
                            StatusCode = "500",
                            SelectionPattern = "5\\d{2}",
                            ResponseTemplates = new Dictionary<string, string>
                            {
                                ["application/json"] = @"{""message"": ""Internal server error""}"
                            }
                        }
                    }
                }
            });

            promptById.AddMethod("DELETE", deleteIntegration, new MethodOptions
            {
                ApiKeyRequired = true,
                MethodResponses = new[]
                {
                    new MethodResponse { StatusCode = "200" },
                    new MethodResponse { StatusCode = "404" },
                    new MethodResponse { StatusCode = "500" }
                }
            });

            new CfnOutput(this, "ApiUrl", new CfnOutputProps
            {
                Value = api.Url,
                Description = "API Gateway URL"
            });

            new CfnOutput(this, "ReadOnlyApiKey", new CfnOutputProps
            {
                Value = apiKey1.KeyId,
                Description = "ReadOnly API Key ID"
            });

            new CfnOutput(this, "AdminApiKey", new CfnOutputProps
            {
                Value = apiKey2.KeyId,
                Description = "Admin API Key ID"
            });
        }
    }
}