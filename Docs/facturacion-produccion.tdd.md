# Evidencia TDD: facturación manual y producción

## Alcance

Los criterios se derivaron de la conversación: registrar una venta sin emitir,
permitir la emisión solo al administrador, separar QA de producción y numerar
las nuevas series `B002`/`F002` desde `1`.

## Recorrido RED/GREEN

| Garantía | Prueba | RED | GREEN |
|---|---|---|---|
| Registrar una venta no llama a Factiliza | `Tests/Program.cs` | La venta terminó `Resultado incierto QA` y hubo 1 llamada | La venta queda `Pendiente`, con 0 llamadas |
| Solo el administrador puede emitir | Reflexión del atributo de `EmitirComprobante` | Faltaba el rol requerido | `Authorize(Roles = "Administrador")` presente |
| Producción usa URL y series correctas | Emisión con HTTP simulado | El servicio solo admitía QA | Usa `apife.factiliza.com`, `B002-00000001` y `F002-00000001` |
| Un resultado incierto no se reenvía | Segundo `Emitir` sobre el mismo registro | Cobertura ausente | El contador HTTP permanece en 1 |
| Producción exige token propio y series confirmadas | Configuraciones incompletas simuladas | Cobertura ausente | `Habilitada` permanece `false` |
| Producción exige contraseña vigente | Controlador con Identity y HTTP simulado | Cobertura ausente | Contraseña errónea: 0 llamadas; correcta: 1 llamada |
| Intentos fallidos bloquean al administrador | Cinco contraseñas erróneas | Cobertura ausente | Identity marca la cuenta bloqueada y no permite emitir |

## Comandos ejecutados

```powershell
dotnet run --project Tests/Comercial.IntegrationTests.csproj -c Release
dotnet build MiniMarket.Web.csproj -c Release
dotnet list MiniMarket.Web.csproj package --vulnerable --include-transitive
```

Resultado final: todas las pruebas pasaron, compilación con 0 errores y 0
advertencias, y NuGet no reportó paquetes vulnerables en los orígenes actuales.

## Límites

El ejecutor de integración de este proyecto no genera métricas de cobertura.
Las llamadas a Factiliza fueron simuladas; no se utilizó ningún token ni se
emitió un comprobante real durante la validación.
