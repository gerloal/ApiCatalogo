# Configuración de Handlers para Lambda Functions

## AssemblyName Actual
El proyecto está compilado con el nombre de ensamblado: **`ProcessFileOnQueue`**

Esto se define en `FuncionLambda.csproj`:
```xml
<AssemblyName>ProcessFileOnQueue</AssemblyName>
```

## Handlers Correctos

### Formato del Handler
```
AssemblyName::Namespace.Clase::Método
```

### Funciones Lambda

| Función Lambda | Handler Correcto |
|----------------|------------------|
| **OrderExportAPI-dev** | `ProcessFileOnQueue::FuncionLambda.LambdaServices::FunctionHandler` |
| **OrderExportWorker-dev** | `ProcessFileOnQueue::FuncionLambda.OrderExportWorker::FunctionHandler` |
| **UnifiedSQSHandler** | `ProcessFileOnQueue::FuncionLambda.UnifiedSQSHandler::FunctionHandler` |

## Scripts para Actualizar Handlers

### Opción 1: Actualizar SOLO los handlers (sin redesplegar código)
```powershell
.\update-handlers-only.ps1
```
Este script actualiza únicamente la configuración del handler sin tocar el código desplegado.

### Opción 2: Redesplegar completamente
```powershell
.\rebuild-and-deploy.ps1
```
Este script recompila, empaqueta y redespliega todo.

### Opción 3: Manual desde CLI
```bash
# OrderExportAPI
aws lambda update-function-configuration \
    --function-name OrderExportAPI-dev \
    --handler "ProcessFileOnQueue::FuncionLambda.LambdaServices::FunctionHandler" \
    --region eu-west-1

# OrderExportWorker
aws lambda update-function-configuration \
    --function-name OrderExportWorker-dev \
    --handler "ProcessFileOnQueue::FuncionLambda.OrderExportWorker::FunctionHandler" \
    --region eu-west-1

# UnifiedSQSHandler
aws lambda update-function-configuration \
    --function-name UnifiedSQSHandler \
    --handler "ProcessFileOnQueue::FuncionLambda.UnifiedSQSHandler::FunctionHandler" \
    --region eu-west-1
```

## Verificar Configuración Actual

```powershell
# Ver handler de una función
aws lambda get-function-configuration \
    --function-name OrderExportAPI-dev \
    --region eu-west-1 \
    --query 'Handler' \
    --output text

# Ver configuración completa
aws lambda get-function-configuration \
    --function-name OrderExportAPI-dev \
    --region eu-west-1 \
    --query '{Handler: Handler, Runtime: Runtime, Timeout: Timeout, Memory: MemorySize}'
```

## Errores Comunes

### Error: "Could not find the specified handler assembly"
**Causa**: El handler apunta a un AssemblyName incorrecto.

**Solución**: Verificar que el handler use `ProcessFileOnQueue` como AssemblyName.

**Incorrecto**: `FuncionLambda::FuncionLambda.LambdaServices::FunctionHandler`  
**Correcto**: `ProcessFileOnQueue::FuncionLambda.LambdaServices::FunctionHandler`

### Error: Assembly encontrado pero clase no existe
**Causa**: El namespace o nombre de clase es incorrecto.

**Solución**: Verificar que las clases estén en el namespace `FuncionLambda`:
- `FuncionLambda.LambdaServices`
- `FuncionLambda.OrderExportWorker`
- `FuncionLambda.UnifiedSQSHandler`

## Cambiar el AssemblyName (Alternativa)

Si prefieres cambiar el AssemblyName a `FuncionLambda`, edita `FuncionLambda.csproj`:

```xml
<AssemblyName>FuncionLambda</AssemblyName>
```

Luego actualiza los handlers a:
```
FuncionLambda::FuncionLambda.LambdaServices::FunctionHandler
FuncionLambda::FuncionLambda.OrderExportWorker::FunctionHandler
FuncionLambda::FuncionLambda.UnifiedSQSHandler::FunctionHandler
```

Y recompila/redespliega completamente.
