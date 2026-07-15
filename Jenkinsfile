
library 'ts-jenkins-shared-library@main'

pipeline {
    agent none
    options {
        copyArtifactPermission('*/TownSuite-Artifact-Publish')
        buildDiscarder(logRotator(numToKeepStr: '10'))
        timestamps()
        timeout(time: 2, unit: 'HOURS')
    }
    stages {
        stage('Start Automation Script') {
            agent { label 'starting-agent' }
            steps {
                script {
                    townsuite_automation2.start_linux_and_windows()
                }
            }
        }    
        stage('Pipeline') {
            parallel {
                stage('Windows Build and Code Signing') {
                    agent { label townsuite_automation2.get_windows_label() }
                    steps {
                        script {
                            townsuite.common_environment_configuration()
                        }

                        pwsh '''
                        ./build_windows.ps1
                        '''
                    
                        // code signing itself
                        withCredentials([
                            string(credentialsId: 'codesigning_service_url', variable: 'CODESIGNING_SERVICE_URL'),
                            string(credentialsId: 'codesigning_auth_key', variable: 'CODESIGNING_AUTH_KEY')
                        ]) {
                            // diagnose DNS/connectivity before signing attempt
                            pwsh '''
                            $url = [System.Uri]$env:CODESIGNING_SERVICE_URL
                            Write-Host "Resolving $($url.Host)..."
                            Resolve-DnsName $url.Host -ErrorAction SilentlyContinue | Format-Table -AutoSize
                            Write-Host "Checking health endpoint..."
                            Invoke-RestMethod -Uri "$($url.Scheme)://$($url.Host):$($url.Port)/healthz" -SkipCertificateCheck | ConvertTo-Json
                            '''

                            pwsh '''
                            $CodeSigningClient = ".\\TownSuite.CodeSigning.Client\\bin\\Release\\net10.0\\TownSuite.CodeSigning.Client.exe"
                            & $CodeSigningClient -rfolder "build|*TownSuite*.dll;*TownSuite*.exe" -url "$env:CODESIGNING_SERVICE_URL" -timeout 60000 -token "$env:CODESIGNING_AUTH_KEY" -ignorecerts
                            '''
                        }

                        pwsh '''
                            (Get-AuthenticodeSignature -FilePath "build\\win-x64\\TownSuite.CodeSigning.Service\\TownSuite.CodeSigning.Service.dll").Status
                            (Get-AuthenticodeSignature -FilePath "build\\win-x64\\TownSuite.CodeSigning.Client\\TownSuite.CodeSigning.Client.dll").Status
                            (Get-AuthenticodeSignature -FilePath "build\\win-arm64\\TownSuite.CodeSigning.Service\\TownSuite.CodeSigning.Service.dll").Status
                            (Get-AuthenticodeSignature -FilePath "build\\win-arm64\\TownSuite.CodeSigning.Client\\TownSuite.CodeSigning.Client.dll").Status
                        '''

                        // zip and hashes
                        pwsh '''
                        # zip the win-x64 and win-arm64 folders
                        $version = ([regex]::Match((Get-Content -Path .\\Directory.Build.props -Raw), '<Version>([0-9.]+)</Version>').Groups[1].Value)
                        cd build
                        Compress-Archive -Path "win-x64\\TownSuite.CodeSigning.Client\\*" -DestinationPath "TownSuite.CodeSigning.Client-$version-win-x64.zip"
                        Compress-Archive -Path "win-x64\\TownSuite.CodeSigning.Service\\*" -DestinationPath "TownSuite.CodeSigning.Service-$version-win-x64.zip"
                        Compress-Archive -Path "win-arm64\\TownSuite.CodeSigning.Client\\*" -DestinationPath "TownSuite.CodeSigning.Client-$version-win-arm64.zip"
                        Compress-Archive -Path "win-arm64\\TownSuite.CodeSigning.Service\\*" -DestinationPath "TownSuite.CodeSigning.Service-$version-win-arm64.zip"

                        # create *.SHA256SUMS per file
                        Get-ChildItem -Path "*.zip" | ForEach-Object {
                            $filePath = $_.FullName
                            $hash = Get-FileHash -Path $filePath -Algorithm SHA256
                            $hashString = $hash.Hash
                            $hashString | Out-File -FilePath "$filePath.SHA256SUMS" -Encoding ascii
                        }
                        '''

                        echo 'archiving artifacts'
                        script {
                            townsuite.archiveWithRetryAndLock('build/*.zip,build/*.SHA256SUMS,build/parameterproperties.txt', 3)
                        }
                    }
                }
                stage('Linux Build and Code Signing') {
                    agent { label townsuite_automation2.get_ubuntu_label() }
                    steps {
                        script {
                            townsuite.common_environment_configuration()
                        }

                        sh '''
                        apt update
                        apt install -y zip ruby osslsigncode
                        chmod +x ./build_linux.sh
                        ./build_linux.sh
                        '''

                        // code signing with the linux client build; the client verifies each
                        // downloaded file client-side (cross-platform PE Authenticode check)
                        // and exits non-zero if verification fails
                        withCredentials([
                            string(credentialsId: 'codesigning_service_url', variable: 'CODESIGNING_SERVICE_URL'),
                            string(credentialsId: 'codesigning_auth_key', variable: 'CODESIGNING_AUTH_KEY')
                        ]) {
                            // diagnose DNS/connectivity before signing attempt
                            sh '''
                            HOST=$(echo "$CODESIGNING_SERVICE_URL" | sed -E 's|^[a-z]+://([^:/]+).*|\\1|')
                            echo "Resolving $HOST..."
                            getent hosts "$HOST" || true
                            echo "Checking health endpoint..."
                            curl -sk "$(echo "$CODESIGNING_SERVICE_URL" | sed -E 's|(/sign)?/*$||')/healthz"
                            '''

                            sh '''
                            CodeSigningClient="./build/linux-x64/TownSuite.CodeSigning.Client/TownSuite.CodeSigning.Client"
                            chmod +x "$CodeSigningClient"
                            "$CodeSigningClient" -rfolder "build|*TownSuite*.dll;*TownSuite*.exe" -excludefolders "pkg-linux-amd64;pkg-linux-arm64" -url "$CODESIGNING_SERVICE_URL" -timeout 60000 -token "$CODESIGNING_AUTH_KEY" -ignorecerts
                            '''
                        }

                        // informational signature status, mirrors the windows
                        // Get-AuthenticodeSignature step
                        sh '''
                        osslsigncode verify -in "build/linux-x64/TownSuite.CodeSigning.Service/TownSuite.CodeSigning.Service.dll" || true
                        osslsigncode verify -in "build/linux-arm64/TownSuite.CodeSigning.Service/TownSuite.CodeSigning.Service.dll" || true
                        '''

                        // build_linux.sh zips before signing runs, so re-pack the Service zips
                        // (the only ones whose contents changed) with the signed dlls. The
                        // Client zips/debs hold a single-file ELF which is not Authenticode
                        // signable and are left as built.
                        sh '''
                        VERSION=$(cat Directory.Build.props | grep "<Version>" | sed 's/[^0-9.]*//g')
                        cd build
                        rm -f "TownSuite.CodeSigning.Service-$VERSION-linux-x64.zip" "TownSuite.CodeSigning.Service-$VERSION-linux-arm64.zip"
                        zip -r "TownSuite.CodeSigning.Service-$VERSION-linux-x64.zip" linux-x64/TownSuite.CodeSigning.Service
                        zip -r "TownSuite.CodeSigning.Service-$VERSION-linux-arm64.zip" linux-arm64/TownSuite.CodeSigning.Service
                        sha256sum "TownSuite.CodeSigning.Service-$VERSION-linux-x64.zip" > "TownSuite.CodeSigning.Service-$VERSION-linux-x64.zip.SHA256SUMS"
                        sha256sum "TownSuite.CodeSigning.Service-$VERSION-linux-arm64.zip" > "TownSuite.CodeSigning.Service-$VERSION-linux-arm64.zip.SHA256SUMS"
                        '''

                        echo 'archiving artifacts'
                        script {
                            townsuite.archiveWithRetryAndLock('build/*.zip,build/*.SHA256SUMS', 3)
                        }
                    }
                }
            }
            
        }
    }
    post {
        always {
            CleanupVirtualMachines()
        }
        success {
            echo 'Pipeline executed successfully.'
        }
        failure {
            echo 'Pipeline failed.'
        }
        aborted {
            echo 'Pipeline was aborted.'
        }
    }
}

def CleanupVirtualMachines() {
    node('stopping-agent') {
        cleanWs()
        script {
            townsuite_automation2.stop_automation()
        }
    }
}
