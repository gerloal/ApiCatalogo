$REGION = "eu-west-1"
$ACCOUNT_ID = (aws sts get-caller-identity --query Account --output text)
# Si tienes Terraform instalado en esa carpeta:
# $ECR_REPO_URL = (terraform output -raw ecr_repository_url)
# O pégalo manualmente si lo sabes, por ej:
$ECR_REPO_URL ="340663646958.dkr.ecr.eu-west-1.amazonaws.com/mi-api-catalogo"
$IMAGE_NAME = "api-catalogo"
$IMAGE_TAG  = "prod-1"
$IMAGE_NAME = "api-catalogo"
$IMAGE_TAG  = "prod-1"

# (opcional) comprueba que el csproj está aquí
Get-ChildItem -Name *.csproj

# construye (ojo al punto final . que indica el contexto)
docker build -t ${IMAGE_NAME}:${IMAGE_TAG} .
