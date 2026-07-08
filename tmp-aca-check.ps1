function global:az { & 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd' @args }
Write-Host '=== ACA live state (latestRevisionName + active image) ==='
az containerapp show --name servantsync --resource-group ServantSync --query 'properties.latestRevisionName' -o tsv
Write-Host ''
Write-Host '=== Active revision(s) (full list) ==='
az containerapp revision list --name servantsync --resource-group ServantSync --query '[].{name:name, created:createdTime, image:properties.template.containers[0].image, status:properties.status, traffic:properties.trafficWeight}' -o json
Write-Host ''
Write-Host '=== Active trailing traffic weights ==='
az containerapp show --name servantsync --resource-group ServantSync --query 'properties.latestRevisionName' -o tsv | ForEach-Object {
  Write-Host ('latest revision: ' + $_)
}
