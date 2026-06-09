
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
                stage('Linux Build') {
                    agent { label townsuite_automation2.get_ubuntu_label() }
                    steps {
                        script {
                            townsuite.common_environment_configuration()
                        }

                        sh '''
                        apt update
                        apt install -y zip ruby
                        chmod +x ./build_linux.sh
                        ./build_linux.sh
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
