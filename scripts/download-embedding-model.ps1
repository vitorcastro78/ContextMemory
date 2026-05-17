# Descarrega all-MiniLM-L6-v2 (ONNX + vocab) para src/ContextMemory.Embeddings/models/
$ErrorActionPreference = "Stop"
$target = Join-Path $PSScriptRoot "..\src\ContextMemory.Embeddings\models"
New-Item -ItemType Directory -Force -Path $target | Out-Null

$vocabUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt"
$modelUrl = "https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx"

Write-Host "Downloading vocab.txt..."
Invoke-WebRequest -Uri $vocabUrl -OutFile (Join-Path $target "vocab.txt")

Write-Host "Downloading model.onnx (pode demorar)..."
Invoke-WebRequest -Uri $modelUrl -OutFile (Join-Path $target "model.onnx")

Write-Host "Concluído: $target"
