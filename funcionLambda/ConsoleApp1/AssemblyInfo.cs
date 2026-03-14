using Amazon.Lambda.Core;
using System.Runtime.CompilerServices;

// Este atributo se aplica a nivel de ensamblado (assembly)
// Solo debe declararse UNA VEZ en todo el proyecto
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]
[assembly: InternalsVisibleTo("FuncionLambda.Tests")]
