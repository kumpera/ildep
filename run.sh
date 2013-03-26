mcs -sdk:4 -debug -r:Mono.Cecil Main.cs Mono.Linker/*.cs Mono.Linker.Steps/*.cs -out:ildep.exe && mono --debug ildep.exe $1 $2 $3 $4
