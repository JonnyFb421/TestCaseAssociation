<!-- PROJECT LOGO -->
<br />
<p align="center">
  <a href="https://github.com/othneildrew/Best-README-Template">
    <img src="http://clipart-library.com/images/AibrraarT.png" alt="Logo">
  </a>

  <h3 align="center">Test Case Association</h3>

  <p align="center">
    This project is used in the build pipeline to create associations between automated tests and Azure Devops Test Cases.
    <br />
  </p>
</p>



<!-- TABLE OF CONTENTS -->
## Table of Contents

* [About the Project](#about-the-project)
* [Usage](#usage)


<!-- ABOUT THE PROJECT -->
## About The Project

The [Azure Devops Test Case Association Guide](https://docs.microsoft.com/en-us/azure/devops/test/associate-automated-test-with-test-case?view=azure-devops) shows us how to link test cases from Visual Studio to Azure Devops maually,
but does not proivde an automated solution.    
Currently this build step is coupled to an automation framework, but eventually the goal to pull the TestCaseID custom attribute into a Nuget package, and the build pipeline logic into an Azure Marketplace plugin.  


Using this build task will allow you enable you to do the following:  
* Run automated tests via [Azure Devops Test Plans](https://docs.microsoft.com/en-us/azure/devops/test/run-automated-tests-from-test-hub?view=azure-devops)  
* Enhance reporting & trending history for test runs  
* Ensure 1:1 relationship between automated tests and Test Cases  


<!-- USAGE EXAMPLES -->
## Usage

Test Case Association executes in two phases: when opening a PR, and when merging a PR.

### Opening a PR
When a PR is opened, Test Case Association will reach out to Azure Devops to validate that the TestCaseIds contain appropriate values.  
This step only does validation, it does NOT create the association in Azure Devops.

PR Pipeline Environment Variables: 
```
// REQUIRED
AZURE_DEVOPS_PAT=your-azure-devops-personal-access-token
// Optional
MAX_MISSING_TEST_CASE_IDS=10
```

### Merging a PR
When a PR is merged, Test Case Association will create the associations in Azure Devops.  
Note: This step also does the validations, but that will likely change in the future. 

Merge Pipeline Environment Variables: 
```
// REQUIRED
AZURE_DEVOPS_PAT=your-azure-devops-personal-access-token
IS_STAGING_BUILD=true
// Optional
MAX_MISSING_TEST_CASE_IDS=10
```
