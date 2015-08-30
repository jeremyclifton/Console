<#
    .SYNOPSIS
        Gets the results of a script session execution.

    .DESCRIPTION

        Receive-ScriptSession.
        The Receive-ScriptSession cmdlet gets the results of Windows PowerShell script sessions, such as those started by using the Start-ScriptSession. 
        You can get the results of all script sessions by their ID or by submitting a script session objects.
        When you start a new script session, the session starts, but the results do not appear immediately. Instead, the command returns an object that represents the script session. The script session object contains useful information about the script session, but it does not contain the results. This method allows you to continue working in the current session while the script session runs. For more information about script sessions in Windows PowerShell, see about_script sessions.
        The Receive-ScriptSession cmdlet gets the results that have been generated by the time that the Receive-ScriptSession command is submitted. If the session is not yet complete an error is raised. You can run additional Receive-ScriptSession commands to get the execution results.
        By default, script session results and the session itself (unless it is a persistent session) are deleted from the system when you receive them, but you can use the KeepResult and KeepSession parameters to save the results so that you can receive them again. To delete the script session results, run the Receive-ScriptSession command again (without the KeepResult parameter), close the session, or use the Remove-ScriptSession cmdlet to delete the script session from the server.

    .PARAMETER KeepResult
        Saves the script session results in the system, even after you have received them. By default, the script session results and the session itself are deleted when they are retrieved. -KeepResult parameter has the -KeepSession parameter implied in its usage.

    .PARAMETER KeepSession
        Causes the receiving of the session results to not destroy the session after the cmdlet has executed.

    .PARAMETER Id
        Gets the results of script session with the specified IDs.
       The ID is a string that uniquely identifies the script session within the server. You can type one or more IDs (separated by commas). To find the ID of a script session, type "Get-ScriptSession" without parameters.

    .PARAMETER Session
        Specifies the script session for which results are being retrieved. Enter a variable that contains the script session or a command that gets the script session. You can also pipe a script session object to Receive-ScriptSession.

    .INPUTS
        Sitecore.Data.Items.Item
    
    .OUTPUTS
        

    .NOTES
        Help Author: Adam Najmanowicz, Michael West

    .LINK
        https://github.com/SitecorePowerShell/Console/

    .EXAMPLE
        PS master:\> Receive-ScriptSession -Path master:\content\home
#>
