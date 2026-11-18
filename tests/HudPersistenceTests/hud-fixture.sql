CREATE TABLE plus_active (steam_id varchar(18) PRIMARY KEY, expires_at datetime NOT NULL) ENGINE=InnoDB;
CREATE TABLE cw_site_selections (steamid64 varchar(18) NOT NULL, weapon_team int NOT NULL, weapon varchar(64) NOT NULL, model varchar(128) NOT NULL, revision bigint NOT NULL, PRIMARY KEY(steamid64,weapon_team,weapon)) ENGINE=InnoDB;
CREATE TABLE cw_skins (model varchar(128) NOT NULL, name varchar(128) NOT NULL, active int NOT NULL, price decimal(12,2) NOT NULL DEFAULT 0, event_only int NOT NULL DEFAULT 0) ENGINE=InnoDB;
CREATE TABLE cw_access (steamid64 varchar(18) NOT NULL, model varchar(128) NOT NULL, sid int NOT NULL, expires bigint NOT NULL) ENGINE=InnoDB;
CREATE TABLE wp_player_graffiti (steamid VARCHAR(64) PRIMARY KEY,graffiti_id INT NOT NULL,tint_id INT NOT NULL) ENGINE=InnoDB;
