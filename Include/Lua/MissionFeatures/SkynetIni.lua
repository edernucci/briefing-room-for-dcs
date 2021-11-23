redIADS = SkynetIADS:create('OPFOR')
redIADS:addSAMSitesByPrefix('SAM')
redIADS:addEarlyWarningRadarsByPrefix('EW')
redIADS:addRadioMenu()
redIADS:activate()

blueIADS = SkynetIADS:create('BLUFOR')
blueIADS:addSAMSitesByPrefix('BLUE-SAM')
blueIADS:addEarlyWarningRadarsByPrefix('BLUE-EW')
blueIADS:addRadioMenu()
blueIADS:activate()

